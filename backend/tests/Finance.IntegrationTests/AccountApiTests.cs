using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Finance.Application.Common;
using Finance.Domain.Accounts;
using Finance.Domain.Transactions;
using Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Finance.IntegrationTests;

public sealed partial class FinanceApiTests
{
    private static async Task<JsonElement[]> Accounts(HttpClient client) =>
        (await client.GetFromJsonAsync<JsonElement>("/api/accounts")).EnumerateArray().ToArray();

    private static async Task<JsonElement> NewAccount(HttpClient client, string name = "Карта", string openingBalance = "100.01")
    {
        var response = await client.PostAsJsonAsync("/api/accounts", new { name, openingBalance });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await Json(response);
    }

    [Fact]
    public async Task Accounts_BalanceTracksCreateMoveEditDelete_AndFiltersByAccount()
    {
        using var client = await User();
        var me = await client.GetFromJsonAsync<JsonElement>("/api/users/me");
        var primary = Assert.Single(await Accounts(client));
        Assert.Equal(me.GetProperty("id").GetGuid(), primary.GetProperty("id").GetGuid());
        var account = await NewAccount(client);
        var id = account.GetProperty("id").GetGuid();
        Assert.Equal("RUB", account.GetProperty("currency").GetString());
        var expenseCategory = await Category(client);
        var incomeCategory = await Category(client, "Income");
        async Task<JsonElement> Add(string categoryId, string type, string amount)
        {
            var response = await client.PostAsJsonAsync("/api/transactions", new { accountId = id, categoryId, type, amount, occurredOn = "2026-10-25" });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return await Json(response);
        }
        var expense = await Add(expenseCategory, "Expense", "10.02");
        await Add(incomeCategory, "Income", "5.01");
        Assert.Equal("95.00", (await Accounts(client)).Single(a => a.GetProperty("id").GetGuid() == id).GetProperty("balance").GetString());
        var filtered = await client.GetFromJsonAsync<JsonElement>($"/api/transactions?accountId={id}");
        Assert.Equal(2, filtered.GetProperty("totalCount").GetInt32());
        var update = await client.PutAsJsonAsync($"/api/transactions/{expense.GetProperty("id").GetGuid()}", new
        {
            accountId = primary.GetProperty("id").GetGuid(), categoryId = expenseCategory, type = "Expense", amount = "20.01",
            occurredOn = "2026-10-25", version = expense.GetProperty("version").GetGuid()
        });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var balances = await Accounts(client);
        Assert.Equal("105.02", balances.Single(a => a.GetProperty("id").GetGuid() == id).GetProperty("balance").GetString());
        Assert.Equal("-20.01", balances.Single(a => a.GetProperty("id").GetGuid() != id).GetProperty("balance").GetString());
        var updated = await Json(update);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/transactions/{updated.GetProperty("id").GetGuid()}?version={updated.GetProperty("version").GetGuid()}")).StatusCode);
        Assert.Equal("0.00", (await Accounts(client)).Single(a => a.GetProperty("id").GetGuid() != id).GetProperty("balance").GetString());
    }

    [Fact]
    public async Task Accounts_EnforceOwnershipUniqueNamesAndVersions_AndPreserveArchivedHistory()
    {
        using var owner = await User();
        using var other = await User();
        var account = await NewAccount(owner, "  Карта  ");
        var id = account.GetProperty("id").GetGuid();
        Assert.DoesNotContain(await Accounts(other), a => a.GetProperty("id").GetGuid() == id);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync("/api/accounts", new { name = "карта", openingBalance = "0" })).StatusCode);
        foreach (var balance in new[] { "1.001", "10000000000000000" })
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync("/api/accounts", new { name = "Некорректный", openingBalance = balance })).StatusCode);
        var version = account.GetProperty("version").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsJsonAsync($"/api/accounts/{id}", new { name = "Чужой", isArchived = true, version })).StatusCode);
        var categoryId = await Category(owner);
        var foreignCategory = await Category(other);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync("/api/transactions", new { accountId = id, categoryId = foreignCategory, type = "Expense", amount = "1", occurredOn = "2026-10-25" })).StatusCode);
        var response = await owner.PostAsJsonAsync("/api/transactions", new { accountId = id, categoryId, type = "Expense", amount = "1", occurredOn = "2026-10-25" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var transaction = await Json(response);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PutAsJsonAsync($"/api/accounts/{id}", new { name = "Карта", isArchived = true, version })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PutAsJsonAsync($"/api/accounts/{id}", new { name = "Устаревшее", isArchived = false, version })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync("/api/transactions", new { accountId = id, categoryId, type = "Expense", amount = "1", occurredOn = "2026-10-25" })).StatusCode);
        // Historical corrections may keep the same archived account.
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync($"/api/transactions/{transaction.GetProperty("id").GetGuid()}", new
        { accountId = id, categoryId, type = "Expense", amount = "2", occurredOn = "2026-10-25", version = transaction.GetProperty("version").GetGuid() })).StatusCode);
        var archived = (await Accounts(owner)).Single(a => a.GetProperty("id").GetGuid() == id);
        Assert.True(archived.GetProperty("isArchived").GetBoolean());
        Assert.Equal("98.01", archived.GetProperty("balance").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PutAsJsonAsync($"/api/accounts/{id}", new { name = "Карта", isArchived = false, version = archived.GetProperty("version").GetGuid() })).StatusCode);
    }

    [Fact]
    public async Task AccountArchivedAfterValidation_CannotReceivePendingTransaction()
    {
        using var client = await User();
        var account = await NewAccount(client);
        var id = account.GetProperty("id").GetGuid();
        var categoryId = Guid.Parse(await Category(client));
        using var scope = fixture.App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        var category = await db.Categories.AsNoTracking().SingleAsync(c => c.Id == categoryId);
        var pending = Finance.Domain.Transactions.Transaction.Create(category.UserId, category, category.Type, 10, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        pending.AssignAccount(await db.Accounts.AsNoTracking().SingleAsync(a => a.Id == id));
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"/api/accounts/{id}", new { name = "Карта", isArchived = true, version = account.GetProperty("version").GetGuid() })).StatusCode);
        var store = new TransactionStore(db);
        store.Add(pending);
        await Assert.ThrowsAsync<ConflictException>(() => store.SaveChangesAsync(CancellationToken.None));
        Assert.Equal("100.01", (await Accounts(client)).Single(a => a.GetProperty("id").GetGuid() == id).GetProperty("balance").GetString());
    }
}
