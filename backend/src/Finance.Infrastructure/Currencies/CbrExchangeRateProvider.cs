using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Finance.Application.Abstractions;
using Finance.Application.Currencies;
using Microsoft.Extensions.Logging;

namespace Finance.Infrastructure.Currencies;

public sealed class CbrExchangeRateProvider(Func<HttpClient> clients, IClock clock, ILogger<CbrExchangeRateProvider> logger)
    : IExchangeRateProvider, IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private ExchangeRates? cached;
    private DateTimeOffset validUntil;
    private DateTimeOffset retryAfter;

    public async Task<ExchangeRates> GetAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (cached is not null && clock.UtcNow < validUntil) return cached;
            if (clock.UtcNow < retryAfter) throw new ExchangeRatesUnavailableException();
            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                using var http = clients();
                using var response = await http.GetAsync("https://www.cbr.ru/scripts/XML_daily.asp", cancellationToken);
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var reader = XmlReader.Create(stream, new XmlReaderSettings
                { Async = true, DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 256_000 });
                var document = await XDocument.LoadAsync(reader, LoadOptions.None, cancellationToken);
                var rates = Parse(document);
                cached = rates;
                validUntil = clock.UtcNow.AddHours(1);
                return rates;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or XmlException or FormatException or OverflowException or InvalidOperationException)
            {
                retryAfter = clock.UtcNow.AddMinutes(1);
                logger.LogWarning(exception, "Could not load display exchange rates from CBR");
                throw new ExchangeRatesUnavailableException();
            }
        }
        finally { gate.Release(); }
    }

    public static ExchangeRates Parse(XDocument document)
    {
        var root = document.Root ?? throw new FormatException("Missing rates root.");
        var date = DateOnly.ParseExact((string?)root.Attribute("Date") ?? "", "dd.MM.yyyy", CultureInfo.InvariantCulture);
        var rates = new Dictionary<string, decimal> { ["RUB"] = 1m };
        foreach (var element in root.Elements("Valute"))
        {
            var code = (string?)element.Element("CharCode");
            if (code is not ("USD" or "EUR")) continue;
            var nominal = decimal.Parse((string?)element.Element("Nominal") ?? "", CultureInfo.GetCultureInfo("ru-RU"));
            var value = decimal.Parse((string?)element.Element("Value") ?? "", CultureInfo.GetCultureInfo("ru-RU"));
            if (nominal <= 0 || value <= 0 || value > 1_000_000_000m || nominal > 1_000_000m)
                throw new FormatException("Invalid exchange rate.");
            var perUnit = value / nominal;
            if (perUnit <= 0 || !rates.TryAdd(code, perUnit)) throw new FormatException("Invalid or duplicate exchange rate.");
        }
        if (!rates.ContainsKey("USD") || !rates.ContainsKey("EUR")) throw new FormatException("Incomplete exchange rates.");
        return new ExchangeRates(date, new ReadOnlyDictionary<string, decimal>(rates));
    }

    public void Dispose() => gate.Dispose();
}
