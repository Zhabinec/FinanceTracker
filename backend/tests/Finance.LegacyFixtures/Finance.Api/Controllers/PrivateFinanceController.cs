using Finance.Application.LegacyFinance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Finance.Api.Controllers;

[ApiController, Authorize, Route("api/private-finance"), RequestSizeLimit(2_800_000)]
public sealed class PrivateFinanceController(PrivateFinanceHandler handler) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Read(CancellationToken ct) => Ok(new { vault = await handler.ReadAsync(ct) });
    [HttpGet("legacy")]
    public async Task<ActionResult<LegacyFinancialExport>> Export(CancellationToken ct) => Ok(await handler.ExportAsync(ct));
    [HttpPost("stage")]
    public async Task<ActionResult<VaultEnvelope>> Stage(StageVault input, CancellationToken ct) => Ok(await handler.StageAsync(input, ct));
    [HttpPost("activate")]
    public async Task<ActionResult<VaultEnvelope>> Activate(RevisionRequest input, CancellationToken ct) => Ok(await handler.ActivateAsync(input.Id, input.Revision, ct));
    [HttpPut]
    public async Task<ActionResult<VaultEnvelope>> Write(WriteVault input, CancellationToken ct) => Ok(await handler.WriteAsync(input, ct));
    [HttpDelete("stage")]
    public async Task<IActionResult> Cancel([FromQuery] Guid id, [FromQuery] long revision, CancellationToken ct)
    { await handler.CancelStageAsync(id, revision, ct); return NoContent(); }
    public sealed record RevisionRequest(Guid Id, long Revision);
}
