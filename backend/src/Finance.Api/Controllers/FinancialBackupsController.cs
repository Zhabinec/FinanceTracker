using Finance.Application.FinancialRecords;
using Finance.Application.Messaging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Finance.Api.Security;
using Finance.Application.Abstractions;
using System.Text.Json;

namespace Finance.Api.Controllers;

[ApiController, Authorize(Policy = "server-finance"), Route("api/finance/backups"), RequestSizeLimit(23_000_000), EnableRateLimiting("financial-backup")]
public sealed class FinancialBackupsController : ControllerBase
{
    [HttpPost("download-ticket")]
    public async Task<IActionResult> PrepareDownload([FromServices] RequestDispatcher<ExportFinancialBackup, EncryptedFinancialBackup> dispatcher,
        [FromServices] FinancialDownloadTickets tickets, [FromServices] ICurrentUser user, CancellationToken ct)
    {
        var backup = await dispatcher.SendAsync(new(), ct);
        var ticket = tickets.Create(user.UserId ?? throw new UnauthorizedAccessException(), JsonSerializer.SerializeToUtf8Bytes(backup, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return Ok(new { url = $"/api/finance/backups/download?ticket={ticket.Token}", fileName = "finance-backup.json", expiresAtUtc = ticket.Expires });
    }
    [HttpGet("download"), HttpHead("download"), AllowAnonymous]
    public IActionResult Download([FromQuery] string? ticket, [FromServices] FinancialDownloadTickets tickets)
    {
        var bytes = tickets.Take(ticket, !HttpMethods.IsHead(Request.Method));
        if (bytes is null) return StatusCode(410);
        Response.Headers.AccessControlAllowOrigin = "https://web.telegram.org";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        return File(bytes, "application/json", "finance-backup.json", enableRangeProcessing: false);
    }
    [HttpPost]
    public Task<EncryptedFinancialBackup> Export([FromServices] RequestDispatcher<ExportFinancialBackup, EncryptedFinancialBackup> dispatcher, CancellationToken ct) => dispatcher.SendAsync(new(), ct);
    [HttpPost("preview")]
    public Task<FinancialBackupPreview> Preview(PreviewFinancialBackup input, [FromServices] RequestDispatcher<PreviewFinancialBackup, FinancialBackupPreview> dispatcher, CancellationToken ct) => dispatcher.SendAsync(input, ct);
    [HttpPost("restore")]
    public async Task<IActionResult> Restore(RestoreFinancialBackup input, [FromServices] RequestDispatcher<RestoreFinancialBackup, bool> dispatcher, CancellationToken ct)
    { await dispatcher.SendAsync(input, ct); return NoContent(); }
}
