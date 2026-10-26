using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Finance.IntegrationTests;

public sealed partial class FinanceApiTests
{
    [Fact]
    public async Task Budgets_CountOnlyOwnedMonthlyExpenses_AndFollowEditsAndDeletion()
    {
        using var client = await User();
        using var other = await User();
        var category = await Category(client);
        var income = await Category(client, "Income");
        var foreign = await Category(other);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/budgets", new { categoryId = category, month = "2026-10-01", limit = "20.00" })).StatusCode);
        var first = await Transaction(client, category, "10.01", occurredOn: "2026-10-01");
        await Transaction(client, category, "15.00", occurredOn: "2026-10-31");
        await Transaction(client, category, "100", occurredOn: "2026-09-30");
        await Transaction(client, category, "100", occurredOn: "2026-11-01");
        await Transaction(client, income, "100", "Income");
        await Transaction(other, foreign, "100");
        var sourceId = (await Accounts(client))[0].GetProperty("id").GetGuid();
        var targetId = (await NewAccount(client)).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/transfers", new { requestId = Guid.NewGuid(), sourceAccountId = sourceId, targetAccountId = targetId, amount = "999" })).StatusCode);
        async Task<JsonElement> Budget() => (await client.GetFromJsonAsync<JsonElement>("/api/budgets?month=2026-10-01"))[0];
        var budget = await Budget();
        Assert.Equal("25.01", budget.GetProperty("spent").GetString());
        Assert.Equal("5.01", budget.GetProperty("overspent").GetString());
        Assert.Equal("0.00", budget.GetProperty("remaining").GetString());
        var transactionId = first.GetProperty("id").GetGuid();
        var changed = await client.PutAsJsonAsync($"/api/transactions/{transactionId}", new { categoryId = category, amount = "1", type = "Expense", occurredOn = "2026-10-01", version = first.GetProperty("version").GetGuid() });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal("16.00", (await Budget()).GetProperty("spent").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/transactions/{transactionId}?version={(await Json(changed)).GetProperty("version").GetGuid()}")).StatusCode);
        budget = await Budget();
        Assert.Equal("15.00", budget.GetProperty("spent").GetString());
        Assert.Equal("5.00", budget.GetProperty("remaining").GetString());
        Assert.Equal("0.00", budget.GetProperty("overspent").GetString());
        Assert.Empty((await client.GetFromJsonAsync<JsonElement>("/api/budgets?month=2026-11-01")).EnumerateArray());
        await client.PostAsJsonAsync($"/api/categories/{category}/deactivate", new { });
        Assert.False((await Budget()).GetProperty("categoryActive").GetBoolean());
    }

    [Fact]
    public async Task Budgets_EnforceOwnershipUniquenessValidationAndVersions()
    {
        using var client = await User();
        using var other = await User();
        var category = await Category(client);
        var payload = new { categoryId = category, month = "2026-10-01", limit = "20" };
        var results = await Task.WhenAll(client.PostAsJsonAsync("/api/budgets", payload), client.PostAsJsonAsync("/api/budgets", payload));
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync("/api/budgets", payload)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/budgets", new { categoryId = await Category(client, "Income"), payload.month, payload.limit })).StatusCode);
        foreach (var amount in new[] { "0", "-1", "1.001", "10000000000000000" })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/budgets", new { payload.categoryId, payload.month, limit = amount })).StatusCode);
        foreach (var month in new[] { "2026-10-02", "0001-01-01", "9999-12-01", "invalid" })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/budgets?month={month}")).StatusCode);
        var budget = (await client.GetFromJsonAsync<JsonElement>("/api/budgets?month=2026-10-01"))[0];
        var id = budget.GetProperty("id").GetGuid();
        var version = budget.GetProperty("version").GetGuid();
        Assert.Empty((await other.GetFromJsonAsync<JsonElement>("/api/budgets?month=2026-10-01")).EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsJsonAsync($"/api/budgets/{id}", new { limit = "30", version })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/budgets/{id}?version={version}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"/api/budgets/{id}", new { limit = "30", version })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/budgets/{id}", new { limit = "40", version })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/budgets/{id}?version={version}")).StatusCode);
        budget = (await client.GetFromJsonAsync<JsonElement>("/api/budgets?month=2026-10-01"))[0];
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/budgets/{id}?version={budget.GetProperty("version").GetGuid()}")).StatusCode);
    }
}
