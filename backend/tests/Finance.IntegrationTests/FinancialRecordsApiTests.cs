using System.Net;
using System.Net.Http.Json;
using Finance.Domain.Ledger;
using Finance.Infrastructure.Persistence;
using Finance.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Finance.IntegrationTests;

public sealed partial class FinanceApiTests
{
    [Fact]
    public async Task RecordsMigrationIsAtomicIdempotentAndBlocksOldClients()
    {
        using var app = ManagedApp().WithWebHostBuilder(b => b.UseSetting("FinanceRecords:Enabled", "true"));
        using var owner = ManagedClient(app);
        var identity = await RegisterManaged(owner);
        await Transaction(owner, await Category(owner), "25.00");
        var source = await Json(await owner.PostAsync("/api/private-finance/key/start", null));
        var revision = source.GetProperty("revision").GetInt64();
        var originalCiphertext = source.GetProperty("ciphertext").GetString();
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync("/api/finance/migrate", new { expectedRevision = revision + 1 })).StatusCode);
        Assert.False((await Json(await owner.GetAsync("/api/finance/status"))).GetProperty("ready").GetBoolean());
        var attempts = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => owner.PostAsJsonAsync("/api/finance/migrate", new { expectedRevision = revision })));
        foreach (var result in attempts) result.EnsureSuccessStatusCode();
        Assert.True((await Json(await owner.GetAsync("/api/finance/status"))).GetProperty("ready").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, (await owner.GetAsync("/api/private-finance")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PutAsJsonAsync("/api/private-finance", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.GetAsync("/api/private-finance/key")).StatusCode);
        var history = await Json(await owner.GetAsync("/api/finance/transactions"));
        Assert.Equal(1, history.GetProperty("totalCount").GetInt32());
        Assert.Equal("25.00", history.GetProperty("items")[0].GetProperty("amount").GetString());
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        Assert.Single(await db.FinancialWorkspaces.Where(x => x.UserId == identity.UserId).ToListAsync());
        Assert.Single(await db.FinancialRecords.Where(x => x.UserId == identity.UserId && x.Kind == FinancialRecordKind.Transaction).ToListAsync());
        Assert.Equal(originalCiphertext, (await db.PrivateFinancialVaults.AsNoTracking().SingleAsync(x => x.UserId == identity.UserId)).Ciphertext);
    }

    [Fact]
    public async Task ServerTransactionsValidateOwnershipRetryVersionsAndUpdateEncryptedBalances()
    {
        using var app = ManagedApp().WithWebHostBuilder(b => b.UseSetting("FinanceRecords:Enabled", "true"));
        using var owner = ManagedClient(app); using var other = ManagedClient(app);
        var identity = await RegisterManaged(owner); await RegisterManaged(other);
        var category = await Category(owner);
        await owner.PostAsync("/api/private-finance/key/start", null);
        (await owner.PostAsJsonAsync("/api/finance/migrate", new { expectedRevision = 1 })).EnsureSuccessStatusCode();
        await other.PostAsync("/api/private-finance/key/start", null);
        (await other.PostAsJsonAsync("/api/finance/migrate", new { expectedRevision = 1 })).EnsureSuccessStatusCode();
        var requestId = Guid.NewGuid();
        object Input(decimal amount, Guid? version = null) => new { accountId = identity.UserId, categoryId = category,
            type = "Expense", amount, description = "Чипсы", requestId, version };
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync("/api/finance/transactions", Input(25))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync("/api/finance/transactions", Input(-25))).StatusCode);
        var firstResponse = await owner.PostAsJsonAsync("/api/finance/transactions", Input(25)); firstResponse.EnsureSuccessStatusCode();
        var first = await Json(firstResponse);
        var id = first.GetProperty("id").GetGuid(); var version = first.GetProperty("version").GetGuid();
        var replay = await Json(await owner.PostAsJsonAsync("/api/finance/transactions", Input(25.00m)));
        Assert.Equal(id, replay.GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync("/api/finance/transactions", Input(30))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PutAsJsonAsync($"/api/finance/transactions/{id}", Input(30, Guid.NewGuid()))).StatusCode);
        var changedResponse = await owner.PutAsJsonAsync($"/api/finance/transactions/{id}", Input(30, version)); changedResponse.EnsureSuccessStatusCode();
        var changed = await Json(changedResponse);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
            var managed = await db.ManagedFinancialKeys.AsNoTracking().SingleAsync(x => x.UserId == identity.UserId);
            using var protection = new FinancialRecordProtection(identity.UserId, scope.ServiceProvider.GetRequiredService<FinancialKeyProtection>().Unprotect(managed));
            var account = await db.FinancialRecords.AsNoTracking().SingleAsync(x => x.UserId == identity.UserId && x.Kind == FinancialRecordKind.Account);
            Assert.Equal(-30m, protection.Decrypt<AccountData>(account).Balance);
            var expense = await db.FinancialRecords.AsNoTracking().SingleAsync(x => x.UserId == identity.UserId && x.Kind == FinancialRecordKind.Transaction);
            Assert.DoesNotContain("Чипсы", System.Text.Encoding.UTF8.GetString(expense.Ciphertext));
        }
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/finance/transactions/{id}?version={changed.GetProperty("version").GetGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync("/api/finance/transactions", Input(25))).StatusCode);
        Assert.Equal(0, (await Json(await owner.GetAsync("/api/finance/transactions"))).GetProperty("totalCount").GetInt32());
    }
    [Fact]
    public async Task DisabledRecordModeCannotBeActivatedByAClientRequest()
    {
        using var app = ManagedApp(); using var owner = ManagedClient(app);
        await RegisterManaged(owner);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PostAsJsonAsync("/api/finance/migrate", new { expectedRevision = 1 })).StatusCode);
    }
}
