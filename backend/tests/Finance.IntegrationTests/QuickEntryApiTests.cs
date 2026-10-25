using System.Net;
using System.Net.Http.Json;
using Finance.Application.Transactions;

namespace Finance.IntegrationTests;

public sealed partial class FinanceApiTests
{
    [Fact]
    public async Task TransactionWithoutDate_UsesServerCreationDate_AndCanBeUndone()
    {
        using var client = await User();
        var categoryId = await Category(client);
        var response = await client.PostAsJsonAsync("/api/transactions", new { categoryId, type = "Expense", amount = "25.00", description = "Чипсы" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var transaction = await Json(response);
        Assert.Equal("25.00", transaction.GetProperty("amount").GetString());
        Assert.Equal("Чипсы", transaction.GetProperty("description").GetString());
        Assert.Equal(MoscowDate.FromUtc(transaction.GetProperty("createdAtUtc").GetDateTimeOffset()).ToString("yyyy-MM-dd"), transaction.GetProperty("occurredOn").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/transactions/{transaction.GetProperty("id").GetGuid()}?version={transaction.GetProperty("version").GetGuid()}")).StatusCode);
        Assert.Equal("0.00", Assert.Single(await Accounts(client)).GetProperty("balance").GetString());
    }
}
