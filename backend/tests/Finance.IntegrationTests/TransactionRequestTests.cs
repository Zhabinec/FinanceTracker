using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Finance.Domain.Transactions;
using Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Finance.IntegrationTests;

public sealed partial class FinanceApiTests
{
    [Fact]
    public async Task ReleaseMigration_BackfillsOnlyEmptyUsers_AndCanRunAgain()
    {
        using var scope = fixture.App.Services.CreateScope();
        var main = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        var databaseName = "finance_migration_" + Guid.NewGuid().ToString("N");
        await using (var admin = new Npgsql.NpgsqlConnection(main.Database.GetConnectionString()))
        {
            await admin.OpenAsync();
            using var identifiers = new Npgsql.NpgsqlCommandBuilder();
            await using var create = new Npgsql.NpgsqlCommand("CREATE DATABASE " + identifiers.QuoteIdentifier(databaseName), admin);
            await create.ExecuteNonQueryAsync();
        }
        var connection = new Npgsql.NpgsqlConnectionStringBuilder(main.Database.GetConnectionString()) { Database = databaseName };
        await using var db = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>().UseNpgsql(connection.ConnectionString).Options);
        await db.GetService<IMigrator>().MigrateAsync("20261026090251_AddImportBatches");
        var emptyUser = Finance.Domain.Users.User.Create("empty@example.test", "hash", DateTimeOffset.UtcNow, "RUB");
        var customUser = Finance.Domain.Users.User.Create("custom@example.test", "hash", DateTimeOffset.UtcNow, "RUB");
        db.Users.AddRange(emptyUser, customUser);
        var custom = Finance.Domain.Categories.Category.Create(customUser.Id, "Своё", TransactionType.Expense);
        custom.Deactivate();
        db.Categories.Add(custom);
        await db.SaveChangesAsync();
        await db.Database.MigrateAsync();
        Assert.Equal(7, await db.Categories.CountAsync(c => c.UserId == emptyUser.Id));
        Assert.Equal(1, await db.Categories.CountAsync(c => c.UserId == customUser.Id));
        Assert.False((await db.Categories.AsNoTracking().SingleAsync(c => c.Id == custom.Id)).IsActive);
        await db.Database.MigrateAsync();
        Assert.Equal(7, await db.Categories.CountAsync(c => c.UserId == emptyUser.Id));
        Assert.Equal(0, await db.Transactions.CountAsync());
    }
    [Fact]
    public async Task TransactionRequest_IsAtomicConcurrentAndSurvivesEditAndDeletion()
    {
        using var client = await User();
        var categoryId = await Category(client);
        var payload = new { requestId = Guid.NewGuid(), categoryId, type = "Expense", amount = "25.01", description = " Чипсы ", occurredOn = (string?)null };
        var results = await Task.WhenAll(client.PostAsJsonAsync("/api/transactions", payload), client.PostAsJsonAsync("/api/transactions", payload));
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        var first = await Json(results[0]);
        var id = first.GetProperty("id").GetGuid();
        Assert.Equal(id, (await Json(results[1])).GetProperty("id").GetGuid());
        Assert.Equal("-25.01", (await Accounts(client))[0].GetProperty("balance").GetString());
        Assert.Equal(1, (await client.GetFromJsonAsync<JsonElement>("/api/transactions")).GetProperty("totalCount").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/transactions", new { payload.requestId, categoryId, payload.type, amount = "30", payload.description })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/transactions", new { requestId = Guid.Empty, categoryId, payload.type, payload.amount })).StatusCode);
        var changed = await client.PutAsJsonAsync($"/api/transactions/{id}", new { categoryId, payload.type, amount = "10", occurredOn = "2026-10-25", version = first.GetProperty("version").GetGuid() });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var current = await Json(changed);
        var replay = await client.PostAsJsonAsync("/api/transactions", payload);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal("10.00", (await Json(replay)).GetProperty("amount").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/transactions/{id}?version={current.GetProperty("version").GetGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/transactions", payload)).StatusCode);
        Assert.Equal(0, (await client.GetFromJsonAsync<JsonElement>("/api/transactions")).GetProperty("totalCount").GetInt32());
        Assert.Equal("0.00", (await Accounts(client))[0].GetProperty("balance").GetString());
    }

    [Fact]
    public async Task TransactionRequest_IsScopedToOwner_AndReplaysAfterCategoryDeactivation()
    {
        using var owner = await User();
        using var other = await User();
        var categoryId = await Category(owner);
        var requestId = Guid.NewGuid();
        var payload = new { requestId, categoryId, type = "Income", amount = "15" };
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync("/api/transactions", payload)).StatusCode);
        var expense = new { requestId, categoryId, type = "Expense", amount = "15" };
        Assert.Equal(HttpStatusCode.Created, (await owner.PostAsJsonAsync("/api/transactions", expense)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync($"/api/categories/{categoryId}/deactivate", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync("/api/transactions", expense)).StatusCode);
        var foreignCategory = await Category(other);
        Assert.Equal(HttpStatusCode.Created, (await other.PostAsJsonAsync("/api/transactions", new { requestId, categoryId = foreignCategory, type = "Expense", amount = "20" })).StatusCode);
        Assert.Equal("-15.00", (await Accounts(owner))[0].GetProperty("balance").GetString());
        Assert.Equal("-20.00", (await Accounts(other))[0].GetProperty("balance").GetString());
    }

    [Fact]
    public async Task Registration_ProvidesPrivateEditableStarterCategories_WithoutFinancialRecords()
    {
        using var owner = await User();
        using var other = await User();
        var categories = (await owner.GetFromJsonAsync<JsonElement>("/api/categories")).EnumerateArray().ToArray();
        Assert.Equal(7, categories.Length);
        Assert.Contains(categories, c => c.GetProperty("name").GetString() == "Продукты" && c.GetProperty("type").GetString() == "Expense");
        Assert.Contains(categories, c => c.GetProperty("name").GetString() == "Зарплата" && c.GetProperty("type").GetString() == "Income");
        Assert.All(categories, c => Assert.True(c.GetProperty("isActive").GetBoolean()));
        var otherIds = (await other.GetFromJsonAsync<JsonElement>("/api/categories")).EnumerateArray().Select(c => c.GetProperty("id").GetGuid()).ToArray();
        Assert.DoesNotContain(categories, c => otherIds.Contains(c.GetProperty("id").GetGuid()));
        var food = categories.Single(c => c.GetProperty("name").GetString() == "Продукты");
        var id = food.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await owner.PatchAsJsonAsync($"/api/categories/{id}", new { name = "Покупки" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync($"/api/categories/{id}/deactivate", new { })).StatusCode);
        Assert.Equal(0, (await owner.GetFromJsonAsync<JsonElement>("/api/transactions")).GetProperty("totalCount").GetInt32());
        Assert.Equal("0.00", (await Accounts(owner))[0].GetProperty("balance").GetString());
    }
}
