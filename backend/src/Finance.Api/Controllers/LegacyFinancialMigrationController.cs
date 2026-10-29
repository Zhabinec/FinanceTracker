using Finance.Application.Abstractions;
using Finance.Application.LegacyFinance;
using Finance.Application.FinancialRecords;
using Finance.Application.Messaging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Finance.Api.Controllers;

[ApiController, Authorize(Policy = "server-finance"), Route("api/finance/legacy"), RequestSizeLimit(2048)]
public sealed class LegacyFinancialMigrationController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Read([FromServices] ICurrentUser user, [FromServices] ILegacyFinancialSource source, CancellationToken ct) =>
        Ok(new { vault = await source.ReadAsync(user.UserId ?? throw new UnauthorizedAccessException(), ct) });
    [HttpPost("enroll")]
    public async Task<IActionResult> Enroll(EnrollLegacyFinancialKey input, [FromServices] RequestDispatcher<EnrollLegacyFinancialKey, bool> dispatcher, CancellationToken ct)
    { await dispatcher.SendAsync(input, ct); return NoContent(); }
}
