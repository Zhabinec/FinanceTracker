namespace Finance.Application.Currencies;

// Rubles per one currency unit. Rates are for display only, never for rewriting history.
public sealed record ExchangeRates(DateOnly Date, IReadOnlyDictionary<string, decimal> RublesPerUnit);

public interface IExchangeRateProvider
{
    Task<ExchangeRates> GetAsync(CancellationToken cancellationToken);
}

public sealed class ExchangeRatesUnavailableException() : Exception("Курсы валют сейчас недоступны. Суммы показаны в исходной валюте.");
