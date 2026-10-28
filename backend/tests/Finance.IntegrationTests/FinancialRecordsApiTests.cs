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
    [Fact]
    public async Task EncryptedDailyTotalsBudgetsAndAccountBalancesFollowServerCommands()
    {
        using var app = ManagedApp().WithWebHostBuilder(b => b.UseSetting("FinanceRecords:Enabled", "true"));
        using var owner = ManagedClient(app);
        var identity = await RegisterManaged(owner);
        var categoryId = await Category(owner);
        (await owner.PostAsync("/api/private-finance/key/start", null)).EnsureSuccessStatusCode();
        (await owner.PostAsJsonAsync("/api/finance/migrate", new { expectedRevision = 1 })).EnsureSuccessStatusCode();
        var createdAccount = await owner.PostAsJsonAsync("/api/finance/accounts", new { name = "Карта", openingBalance = "100.00" }); createdAccount.EnsureSuccessStatusCode();
        var account = await Json(createdAccount); var accountId = account.GetProperty("id").GetGuid();
        var transferResponse = await owner.PostAsJsonAsync("/api/finance/transfers", new { requestId = Guid.NewGuid(), sourceAccountId = identity.UserId,
            targetAccountId = accountId, amount = "25.00", description = "На карту" }); transferResponse.EnsureSuccessStatusCode();
        var transfer = await Json(transferResponse);
        var cancelPath = $"/api/finance/transfers/{transfer.GetProperty("id").GetGuid()}/cancel";
        for (var i = 0; i < 2; i++) (await owner.PostAsJsonAsync(cancelPath, new { version = transfer.GetProperty("version").GetGuid() })).EnsureSuccessStatusCode();
        var today = Finance.Application.Transactions.MoscowDate.FromUtc(DateTimeOffset.UtcNow);
        var month = new DateOnly(today.Year, today.Month, 1);
        (await owner.PostAsJsonAsync("/api/finance/budgets", new { categoryId, month, limit = "5.00" })).EnsureSuccessStatusCode();
        var expenseResponse = await owner.PostAsJsonAsync("/api/finance/transactions", new { requestId = Guid.NewGuid(), accountId, categoryId,
            type = "Expense", amount = "10.00", description = "Чипсы" }); expenseResponse.EnsureSuccessStatusCode();
        var expense = await Json(expenseResponse);
        var dashboard = await Json(await owner.GetAsync($"/api/finance/dashboard?from={month:yyyy-MM-dd}&to={today:yyyy-MM-dd}"));
        Assert.Equal("10.00", dashboard.GetProperty("expense").GetString());
        Assert.Equal("0.00", dashboard.GetProperty("income").GetString());
        var budgets = await Json(await owner.GetAsync($"/api/finance/budgets?month={month:yyyy-MM-dd}"));
        Assert.Equal("10.00", budgets[0].GetProperty("spent").GetString());
        Assert.Equal("5.00", budgets[0].GetProperty("overspent").GetString());
        var accounts = await Json(await owner.GetAsync("/api/finance/accounts"));
        Assert.Equal("90.00", accounts.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == accountId).GetProperty("balance").GetString());
        (await owner.DeleteAsync($"/api/finance/transactions/{expense.GetProperty("id").GetGuid()}?version={expense.GetProperty("version").GetGuid()}")).EnsureSuccessStatusCode();
        Assert.Equal("0.00", (await Json(await owner.GetAsync($"/api/finance/dashboard?from={month:yyyy-MM-dd}&to={today:yyyy-MM-dd}"))).GetProperty("expense").GetString());
        using var scope = app.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<FinanceDbContext>().FinancialRecords.AnyAsync(x => x.UserId == identity.UserId && x.Kind == FinancialRecordKind.DailyTotal));
    }
    [Fact]
    public async Task RecordCsvImportIsAtomicDeduplicatedAndAccumulatesOneDailyBucket()
    {
        using var app = ManagedApp().WithWebHostBuilder(b => b.UseSetting("FinanceRecords:Enabled", "true"));
        using var owner = ManagedClient(app);
        var identity = await RegisterManaged(owner);
        var categoryId = await Category(owner);
        var categories = await Json(await owner.GetAsync("/api/categories?includeInactive=true"));
        var categoryName = categories.EnumerateArray().Single(x => x.GetProperty("id").GetString() == categoryId).GetProperty("name").GetString();
        (await owner.PostAsync("/api/private-finance/key/start", null)).EnsureSuccessStatusCode();
        (await owner.PostAsJsonAsync("/api/finance/migrate", new { expectedRevision = 1 })).EnsureSuccessStatusCode();
        object Csv(string amount) => new { content = $"date;amount;category\n2026-10-29;10.00;{categoryName}\n2026-10-29;{amount};{categoryName}",
            delimiter = ";", accountId = identity.UserId, mapping = new { date = 0, amount = 1, category = 2, description = (int?)null, type = (int?)null }, defaultType = "Expense" };
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync("/api/finance/imports/csv/apply", Csv("invalid"))).StatusCode);
        Assert.Equal(0, (await Json(await owner.GetAsync("/api/finance/transactions"))).GetProperty("totalCount").GetInt32());
        var preview = await Json(await owner.PostAsJsonAsync("/api/finance/imports/csv/preview", Csv("20.00")));
        Assert.True(preview.GetProperty("canApply").GetBoolean());
        var result = await owner.PostAsJsonAsync("/api/finance/imports/csv/apply", Csv("20.00")); result.EnsureSuccessStatusCode();
        Assert.Equal(2, (await Json(result)).GetProperty("count").GetInt32());
        var sorted = await Json(await owner.GetAsync("/api/finance/transactions?sort=amountDesc&page=1&pageSize=1"));
        Assert.Equal("20.00", sorted.GetProperty("items")[0].GetProperty("amount").GetString());
        var secondPage = await Json(await owner.GetAsync("/api/finance/transactions?sort=amountDesc&page=2&pageSize=1"));
        Assert.Equal("10.00", secondPage.GetProperty("items")[0].GetProperty("amount").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync("/api/finance/imports/csv/apply", Csv("20.00"))).StatusCode);
        var totals = await Json(await owner.GetAsync("/api/finance/dashboard?from=2026-10-01&to=2026-10-31"));
        Assert.Equal("30.00", totals.GetProperty("expense").GetString());
        var accounts = await Json(await owner.GetAsync("/api/finance/accounts"));
        Assert.Equal("-30.00", accounts[0].GetProperty("balance").GetString());
        using var scope = app.Services.CreateScope();
        Assert.Single(await scope.ServiceProvider.GetRequiredService<FinanceDbContext>().FinancialRecords.Where(x => x.UserId == identity.UserId && x.Kind == FinancialRecordKind.DailyTotal).ToListAsync());
    }
    [Fact]
    public async Task AConsistentReadSnapshotDoesNotBlockWritingANewExpense()
    {
        using var app = ManagedApp().WithWebHostBuilder(b => b.UseSetting("FinanceRecords:Enabled", "true"));
        using var owner = ManagedClient(app);
        var identity = await RegisterManaged(owner); var categoryId = await Category(owner);
        (await owner.PostAsync("/api/private-finance/key/start", null)).EnsureSuccessStatusCode();
        (await owner.PostAsJsonAsync("/api/finance/migrate", new { expectedRevision = 1 })).EnsureSuccessStatusCode();
        using var scope = app.Services.CreateScope();
        await using var book = await scope.ServiceProvider.GetRequiredService<Finance.Application.FinancialRecords.IFinancialBooks>()
            .OpenAsync(identity.UserId, false, CancellationToken.None);
        Assert.Equal(0m, (await book.AccountAsync(identity.UserId, CancellationToken.None))!.Balance);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        (await owner.PostAsJsonAsync("/api/finance/transactions", new { accountId = identity.UserId, categoryId, type = "Expense",
            amount = "25.00", description = "Чипсы", requestId = Guid.NewGuid() }, deadline.Token)).EnsureSuccessStatusCode();
        Assert.Equal(0, (await book.TransactionsAsync(new(), CancellationToken.None)).Count);
        await book.CommitAsync(CancellationToken.None);
        Assert.Equal(1, (await Json(await owner.GetAsync("/api/finance/transactions"))).GetProperty("totalCount").GetInt32());
    }
}
