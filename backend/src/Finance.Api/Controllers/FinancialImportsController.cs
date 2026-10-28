using Finance.Application.FinancialRecords;
using Finance.Application.Imports;
using Finance.Application.Messaging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Finance.Api.Controllers;

[ApiController, Authorize(Policy = "server-finance"), Route("api/finance/imports/csv"), RequestSizeLimit(2_800_000)]
public sealed class FinancialImportsController : ControllerBase
{
    [HttpPost("preview")]
    public Task<ImportPreview> Preview(ImportCommand input, [FromServices] RequestDispatcher<PreviewFinancialCsv, ImportPreview> dispatcher, CancellationToken ct) =>
        dispatcher.SendAsync(new(input), ct);
    [HttpPost("apply")]
    public Task<ImportResult> Apply(ImportCommand input, [FromServices] RequestDispatcher<ApplyFinancialCsv, ImportResult> dispatcher, CancellationToken ct) =>
        dispatcher.SendAsync(new(input), ct);
}
