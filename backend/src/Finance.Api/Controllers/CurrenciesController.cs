using System.Globalization;
using Finance.Application.Currencies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Finance.Api.Controllers;

[ApiController, Authorize, Route("api/currencies")]
public sealed class CurrenciesController(IExchangeRateProvider provider) : ControllerBase
{
    [HttpGet("rates")]
    public async Task<IActionResult> Rates(CancellationToken cancellationToken)
    {
        var rates = await provider.GetAsync(cancellationToken);
        return Ok(new { date = rates.Date, source = "ЦБ РФ", rublesPerUnit = rates.RublesPerUnit.ToDictionary(
            pair => pair.Key, pair => pair.Value.ToString("0.############################", CultureInfo.InvariantCulture)) });
    }
}
