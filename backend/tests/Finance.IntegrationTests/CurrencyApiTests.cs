using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Finance.Application.Currencies;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Finance.IntegrationTests;

public sealed partial class FinanceApiTests
{
    [Fact]
    public async Task RegisterWithoutCurrency_UsesRubles_AndRateReadDoesNotChangeStoredMoney()
    {
        using var anonymous = fixture.Client();
        var email = $"{Guid.NewGuid():N}@example.com";
        var registration = await anonymous.PostAsJsonAsync("/api/auth/register", new { email, password = "Test-password-123!" });
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        Assert.Equal("RUB", (await Json(registration)).GetProperty("baseCurrency").GetString());
        using var client = await User();
        var categoryId = await Category(client);
        var original = await Transaction(client, categoryId, "123.45");
        using var app = fixture.App.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        { services.RemoveAll<IExchangeRateProvider>(); services.AddSingleton<IExchangeRateProvider>(new FixedRates()); }));
        using var ratesClient = app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await ratesClient.GetAsync("/api/currencies/rates")).StatusCode);
        ratesClient.DefaultRequestHeaders.Authorization = client.DefaultRequestHeaders.Authorization;
        var rates = await ratesClient.GetFromJsonAsync<JsonElement>("/api/currencies/rates");
        Assert.Equal("100", rates.GetProperty("rublesPerUnit").GetProperty("USD").GetString());
        var history = await client.GetFromJsonAsync<JsonElement>("/api/transactions");
        var unchanged = Assert.Single(history.GetProperty("items").EnumerateArray());
        Assert.Equal(original.GetProperty("amount").GetString(), unchanged.GetProperty("amount").GetString());
        Assert.Equal(original.GetProperty("version").GetGuid(), unchanged.GetProperty("version").GetGuid());
    }

    [Fact]
    public async Task UnavailableRates_ReturnActionable503()
    {
        using var client = await User();
        using var app = fixture.App.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        { services.RemoveAll<IExchangeRateProvider>(); services.AddSingleton<IExchangeRateProvider>(new FixedRates(true)); }));
        using var ratesClient = app.CreateClient();
        ratesClient.DefaultRequestHeaders.Authorization = client.DefaultRequestHeaders.Authorization;
        var response = await ratesClient.GetAsync("/api/currencies/rates");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("Курсы валют", (await Json(response)).GetProperty("title").GetString());
    }

    private sealed class FixedRates(bool fail = false) : IExchangeRateProvider
    {
        public Task<ExchangeRates> GetAsync(CancellationToken cancellationToken) => fail
            ? throw new ExchangeRatesUnavailableException()
            : Task.FromResult(new ExchangeRates(new DateOnly(2026, 10, 25), new Dictionary<string, decimal> { ["RUB"] = 1, ["USD"] = 100, ["EUR"] = 150 }));
    }
}
