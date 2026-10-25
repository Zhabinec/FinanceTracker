using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Finance.Application.Common;
using Finance.Domain.Categories;
using Finance.Domain.Transactions;
using Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Finance.IntegrationTests;

public sealed class FinanceApiTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private async Task<HttpClient> User()
    {
        var client = fixture.Client();
        var email = $"{Guid.NewGuid():N}@example.com";
        var registration = await client.PostAsJsonAsync("/api/auth/register", new { email, password = "Test-password-123!", baseCurrency = "RUB" });
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Test-password-123!" });
        login.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await Json(login)).GetProperty("accessToken").GetString());
        return client;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).Clone();

    private static async Task<string> Category(HttpClient client, string type = "Expense")
    {
        var response = await client.PostAsJsonAsync("/api/categories", new { name = $"Category {Guid.NewGuid():N}", type });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await Json(response)).GetProperty("id").GetString()!;
    }

    private static async Task<JsonElement> Transaction(HttpClient client, string categoryId, string amount = "10.50", string type = "Expense", string occurredOn = "2026-10-25")
    {
        var response = await client.PostAsJsonAsync("/api/transactions", new { categoryId, amount, type, occurredOn, description = "Test" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await Json(response);
    }

    [Fact]
    public async Task Authentication_RefreshRotates_LogoutRevokes_AnonymousCannotRead()
    {
        using var anonymous = fixture.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/transactions")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/dashboard")).StatusCode);
        using var client = await User();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/users/me")).StatusCode);
        var refreshed = await client.PostAsJsonAsync("/api/auth/refresh", new { });
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        Assert.Contains("httponly", string.Join(";", refreshed.Headers.GetValues("Set-Cookie")).ToLowerInvariant());
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/logout", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/refresh", new { })).StatusCode);
    }

    [Fact]
    public async Task Transactions_EnforceOwnership_AndRejectStaleVersions()
    {
        using var owner = await User();
        using var other = await User();
        var category = await Category(owner);
        var transaction = await Transaction(owner, category, "9999999999999999.99");
        Assert.Equal("9999999999999999.99", transaction.GetProperty("amount").GetString());
        var id = transaction.GetProperty("id").GetString();
        var version = transaction.GetProperty("version").GetString();
        var update = new { categoryId = category, amount = "22.30", type = "Expense", occurredOn = "2026-10-25", version };
        Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsJsonAsync($"/api/transactions/{id}", update)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/transactions/{id}?version={version}")).StatusCode);
        var changed = await owner.PutAsJsonAsync($"/api/transactions/{id}", update);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PutAsJsonAsync($"/api/transactions/{id}", update)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.DeleteAsync($"/api/transactions/{id}?version={version}")).StatusCode);
        var newVersion = (await Json(changed)).GetProperty("version").GetString();
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/transactions/{id}?version={newVersion}")).StatusCode);
    }

    [Fact]
    public async Task Transactions_ValidateCategoryMoneyDates_AndPreserveInactiveHistory()
    {
        using var client = await User();
        using var other = await User();
        var category = await Category(client);
        var foreignCategory = await Category(other);
        foreach (var amount in new[] { "0", "-1", "0.001", "10000000000000000" })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/transactions", new { categoryId = category, amount, type = "Expense", occurredOn = "2026-10-25" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/transactions", new { categoryId = foreignCategory, amount = "1", type = "Expense", occurredOn = "2026-10-25" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/transactions", new { categoryId = category, amount = "1", type = "Income", occurredOn = "2026-10-25" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/transactions", new { categoryId = category, amount = "1", type = "Expense", occurredOn = "0001-01-01" })).StatusCode);
        var transaction = await Transaction(client, category);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/categories/{category}/deactivate", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/transactions", new { categoryId = category, amount = "1", type = "Expense", occurredOn = "2026-10-25" })).StatusCode);
        var changed = await client.PutAsJsonAsync($"/api/transactions/{transaction.GetProperty("id").GetString()}", new {
            categoryId = category, amount = "12", type = "Expense", occurredOn = "2026-10-25", version = transaction.GetProperty("version").GetString() });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
    }

    [Fact]
    public async Task FilteringPagingAndDashboard_UseOwnedInclusiveMoscowPeriod()
    {
        using var client = await User();
        using var other = await User();
        var expense = await Category(client);
        var income = await Category(client, "Income");
        await Transaction(client, expense, "20.15");
        await Transaction(client, expense, "10.10");
        await Transaction(client, income, "100.00", "Income");
        await Transaction(client, expense, "999.00", occurredOn: "2026-10-26");
        await Transaction(client, expense, "999.00", occurredOn: "2026-10-24");
        await Transaction(other, await Category(other), "999.00");
        var prefix = $"/api/transactions?from=2026-10-25&to=2026-10-25&type=Expense&categoryId={expense}&sort=amountAsc&pageSize=1";
        var first = await client.GetFromJsonAsync<JsonElement>(prefix + "&page=1");
        var second = await client.GetFromJsonAsync<JsonElement>(prefix + "&page=2");
        Assert.Equal(2, first.GetProperty("totalCount").GetInt32());
        Assert.Equal("10.10", first.GetProperty("items")[0].GetProperty("amount").GetString());
        Assert.Equal("20.15", second.GetProperty("items")[0].GetProperty("amount").GetString());
        var dashboard = await client.GetFromJsonAsync<JsonElement>("/api/dashboard?from=2026-10-25&to=2026-10-25");
        Assert.Equal("100.00", dashboard.GetProperty("income").GetString());
        Assert.Equal("30.25", dashboard.GetProperty("expense").GetString());
        Assert.Equal("69.75", dashboard.GetProperty("result").GetString());
        Assert.Equal(2, dashboard.GetProperty("categories").GetArrayLength());
        Assert.Equal(3, dashboard.GetProperty("recent").GetArrayLength());
        var invalid = await client.GetAsync("/api/transactions?pageSize=101");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.True((await Json(invalid)).TryGetProperty("traceId", out _));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/dashboard?from=2026-10-26&to=2026-10-25")).StatusCode);
    }

    [Fact]
    public async Task Database_RejectsConcurrentWrites_AndModelMatchesMigrations()
    {
        using var client = await User();
        var json = await Transaction(client, await Category(client));
        var id = json.GetProperty("id").GetGuid();
        using var scope1 = fixture.App.Services.CreateScope();
        using var scope2 = fixture.App.Services.CreateScope();
        var db1 = scope1.ServiceProvider.GetRequiredService<FinanceDbContext>();
        var db2 = scope2.ServiceProvider.GetRequiredService<FinanceDbContext>();
        Assert.False(db1.Database.HasPendingModelChanges());
        var first = await db1.Transactions.SingleAsync(item => item.Id == id);
        var second = await db2.Transactions.SingleAsync(item => item.Id == id);
        var category1 = await db1.Categories.SingleAsync(item => item.Id == first.CategoryId);
        var category2 = await db2.Categories.SingleAsync(item => item.Id == second.CategoryId);
        first.Update(category1, first.Type, 30m, null, first.OccurredAtUtc);
        second.Update(category2, second.Type, 40m, null, second.OccurredAtUtc);
        await new TransactionStore(db1).SaveChangesAsync(CancellationToken.None);
        await Assert.ThrowsAsync<ConflictException>(() => new TransactionStore(db2).SaveChangesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task RefreshToken_CannotBeConsumedTwiceConcurrently()
    {
        using var client = await User();
        var refresh = await client.PostAsJsonAsync("/api/auth/refresh", new { });
        var cookie = refresh.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
        using var first = fixture.Client(false);
        using var second = fixture.Client(false);
        first.DefaultRequestHeaders.Add("Cookie", cookie);
        second.DefaultRequestHeaders.Add("Cookie", cookie);
        var results = await Task.WhenAll(first.PostAsJsonAsync("/api/auth/refresh", new { }), second.PostAsJsonAsync("/api/auth/refresh", new { }));
        Assert.Single(results, result => result.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, result => result.StatusCode == HttpStatusCode.Unauthorized);
    }
}
