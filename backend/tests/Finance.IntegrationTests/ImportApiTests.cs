using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Finance.Application.Imports;
using Finance.Domain.Transactions;

namespace Finance.IntegrationTests;

public sealed partial class FinanceApiTests
{
    [Fact]
    public async Task CsvImport_IsAtomicIdempotentAndIncludedInBudgetsAndBalances()
    {
        using var client = await User();
        var category = await Category(client);
        var categories = await client.GetFromJsonAsync<JsonElement>("/api/categories");
        var name = categories.EnumerateArray().Single(c => c.GetProperty("id").GetString() == category).GetProperty("name").GetString();
        var accountId = (await Accounts(client))[0].GetProperty("id").GetGuid();
        var content = $"Date;Amount;Category;Description\r\n01.10.2026;25,01;{name};\"Чипсы; сыр\"\r\n31.10.2026;10;{name};Молоко\r\n";
        var command = new ImportCommand(content, ";", accountId, new(0, 1, 2, 3, null), TransactionType.Expense);
        await client.PostAsJsonAsync("/api/budgets", new { categoryId = category, month = "2026-10-01", limit = "30" });
        var preview = await client.PostAsJsonAsync("/api/imports/csv/preview", command);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.True((await Json(preview)).GetProperty("canApply").GetBoolean());
        Assert.Equal(0, (await client.GetFromJsonAsync<JsonElement>("/api/transactions")).GetProperty("totalCount").GetInt32());
        var results = await Task.WhenAll(client.PostAsJsonAsync("/api/imports/csv/apply", command), client.PostAsJsonAsync("/api/imports/csv/apply", command));
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
        var history = await client.GetFromJsonAsync<JsonElement>("/api/transactions");
        Assert.Equal(2, history.GetProperty("totalCount").GetInt32());
        Assert.Equal("-35.01", (await Accounts(client))[0].GetProperty("balance").GetString());
        var budget = (await client.GetFromJsonAsync<JsonElement>("/api/budgets?month=2026-10-01"))[0];
        Assert.Equal("35.01", budget.GetProperty("spent").GetString());
        Assert.Equal("5.01", budget.GetProperty("overspent").GetString());
        var normalizedReplay = command with { Content = "\uFEFF" + content.Replace("\r\n", "\n") };
        var repeated = await Json(await client.PostAsJsonAsync("/api/imports/csv/preview", normalizedReplay));
        Assert.True(repeated.GetProperty("alreadyImported").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/imports/csv/apply", normalizedReplay)).StatusCode);
    }

    [Fact]
    public async Task CsvImport_RejectsInvalidRowsForeignAccountsAndInactiveCategoriesWithoutPartialWrites()
    {
        using var client = await User();
        using var other = await User();
        var category = await Category(client);
        var name = (await client.GetFromJsonAsync<JsonElement>("/api/categories")).EnumerateArray().Single(c => c.GetProperty("id").GetString() == category).GetProperty("name").GetString();
        var accountId = (await Accounts(client))[0].GetProperty("id").GetGuid();
        var command = new ImportCommand($"Date;Amount;Category\n2026-10-25;1;{name}\n2026-02-30;-1;Unknown", ";", accountId, new(0, 1, 2, null, null), TransactionType.Expense);
        var preview = await Json(await client.PostAsJsonAsync("/api/imports/csv/preview", command));
        Assert.False(preview.GetProperty("canApply").GetBoolean());
        Assert.Equal(3, preview.GetProperty("rows")[1].GetProperty("errors").GetArrayLength());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/imports/csv/apply", command)).StatusCode);
        Assert.Equal(0, (await client.GetFromJsonAsync<JsonElement>("/api/transactions")).GetProperty("totalCount").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, (await other.PostAsJsonAsync("/api/imports/csv/preview", command)).StatusCode);
        command = command with { Content = $"Date;Amount;Category\n2026-10-25;1;{name}" };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/imports/csv/preview", command with { Mapping = new(0, 0, 2, null, null) })).StatusCode);
        await client.PostAsJsonAsync($"/api/categories/{category}/deactivate", new { });
        Assert.False((await Json(await client.PostAsJsonAsync("/api/imports/csv/preview", command))).GetProperty("canApply").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/imports/csv/apply", command)).StatusCode);
        Assert.Equal(0, (await client.GetFromJsonAsync<JsonElement>("/api/transactions")).GetProperty("totalCount").GetInt32());
    }
}
