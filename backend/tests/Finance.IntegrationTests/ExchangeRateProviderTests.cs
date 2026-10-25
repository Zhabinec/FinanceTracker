using System.Net;
using System.Text;
using System.Xml.Linq;
using Finance.Application.Abstractions;
using Finance.Application.Currencies;
using Finance.Infrastructure.Currencies;
using Microsoft.Extensions.Logging.Abstractions;

namespace Finance.IntegrationTests;

public sealed class ExchangeRateProviderTests
{
    private const string Xml = """
        <?xml version="1.0" encoding="windows-1251"?>
        <ValCurs Date="25.10.2026"><Valute><CharCode>USD</CharCode><Nominal>10</Nominal><Value>1000,0000</Value></Valute>
        <Valute><CharCode>EUR</CharCode><Nominal>100</Nominal><Value>15000,0000</Value></Valute></ValCurs>
        """;

    [Fact]
    public async Task Rates_RespectNominalAndEncoding_AndShareCachedSnapshot()
    {
        var handler = new Reply(Xml);
        var clock = new Clock();
        using var provider = Provider(handler, clock);
        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => provider.GetAsync(CancellationToken.None)));
        Assert.Equal(1, handler.Calls);
        Assert.All(results, rates => { Assert.Equal(100m, rates.RublesPerUnit["USD"]); Assert.Equal(150m, rates.RublesPerUnit["EUR"]); Assert.Equal(1m, rates.RublesPerUnit["RUB"]); });
        clock.UtcNow = clock.UtcNow.AddHours(2);
        await provider.GetAsync(CancellationToken.None);
        Assert.Equal(2, handler.Calls);
    }

    [Theory]
    [InlineData("<ValCurs Date=\"25.10.2026\" />")]
    [InlineData("<!DOCTYPE foo [<!ENTITY xxe SYSTEM 'file:///not-readable'>]><ValCurs Date=\"25.10.2026\">&xxe;</ValCurs>")]
    public async Task InvalidXml_FailsClearly_AndDoesNotFloodProvider(string xml)
    {
        var handler = new Reply(xml);
        using var provider = Provider(handler, new Clock());
        await Assert.ThrowsAsync<ExchangeRatesUnavailableException>(() => provider.GetAsync(CancellationToken.None));
        await Assert.ThrowsAsync<ExchangeRatesUnavailableException>(() => provider.GetAsync(CancellationToken.None));
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void NonPositiveRates_AreRejected(string value)
    {
        var document = XDocument.Parse(Xml.Replace("1000,0000", value));
        Assert.Throws<FormatException>(() => CbrExchangeRateProvider.Parse(document));
    }

    [Fact]
    public async Task Cancellation_IsPreserved()
    {
        using var provider = Provider(new Reply(Xml), new Clock());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetAsync(cancellation.Token));
    }

    private static CbrExchangeRateProvider Provider(Reply handler, Clock clock) => new(
        () => new HttpClient(handler, false), clock, NullLogger<CbrExchangeRateProvider>.Instance);
    private sealed class Clock : IClock { public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 25, 0, 0, 0, TimeSpan.Zero); }
    private sealed class Reply(string xml) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new ByteArrayContent(Encoding.GetEncoding(1251).GetBytes(xml)) });
        }
    }
}
