using Finance.Application.LegacyFinance;
using Finance.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Finance.Api.Controllers;

[ApiController, Authorize, Route("api/private-finance/key"), RequestSizeLimit(2048), EnableRateLimiting("telegram-auth")]
public sealed class ManagedFinancialKeyController(ManagedKeyHandler handler, FinancialKeySettings settings) : ControllerBase
{
    private bool Allowed => settings.Enabled && User.HasClaim(c => c.Type == "telegram_link");
    [HttpPost("start")]
    public async Task<IActionResult> Start(CancellationToken ct)
    {
        if (!Allowed) return NotFound();
        return Ok(await handler.StartAsync(ct));
    }
    [HttpGet]
    public async Task<IActionResult> Read(CancellationToken ct)
    {
        if (!Allowed) return NotFound();
        return Ok(new { code = await handler.ReadAsync(ct) });
    }
    [HttpPost]
    public async Task<IActionResult> Enroll(EnrollFinancialKey input, CancellationToken ct)
    {
        if (!Allowed) return NotFound();
        await handler.EnrollAsync(input, ct);
        return NoContent();
    }
}
