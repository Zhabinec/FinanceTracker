using Finance.Application.Abstractions;
using Finance.Application.Auth.Telegram;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Finance.Api.Controllers;

[ApiController]
[Route("api/auth/telegram")]
[RequestSizeLimit(16384)]
[EnableRateLimiting("telegram-auth")]
public sealed class TelegramAuthController(TelegramAuthHandler handler, ITelegramAuthStore store,
    TelegramSettings settings, ICurrentUser currentUser, IClock clock) : ControllerBase
{
    [HttpGet("options")]
    public IActionResult Options() => Ok(new { settings.Enabled, settings.BotUsername });

    [HttpPost("register")]
    public async Task<ActionResult<TelegramTokens>> Register(LaunchRequest request, CancellationToken ct) =>
        Ok(await handler.RegisterAsync(request.InitData, ct));

    [HttpPost("login")]
    public async Task<IActionResult> Login(LaunchRequest request, CancellationToken ct)
    {
        var session = await handler.LoginAsync(request.InitData, ct);
        return Ok(new { linkRequired = session is null, session });
    }
    [HttpPost("link")]
    public async Task<ActionResult<TelegramTokens>> Link(LinkRequest request, CancellationToken ct) =>
        Ok(await handler.LinkAsync(request.InitData, request.Email, request.Password, ct));
    [HttpPost("refresh")]
    public async Task<ActionResult<TelegramTokens>> Refresh(TokenRequest request, CancellationToken ct) =>
        Ok(await handler.RefreshAsync(request.RefreshToken, ct));
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(TokenRequest request, CancellationToken ct)
    { await handler.LogoutAsync(request.RefreshToken, ct); return NoContent(); }

    [Authorize]
    [HttpGet("link")]
    public async Task<ActionResult<TelegramLinkStatus>> Status(CancellationToken ct)
    {
        var link = await store.FindLinkAsync(currentUser.UserId!.Value, ct);
        return Ok(new TelegramLinkStatus(link is not null, link?.Version));
    }
    [Authorize]
    [HttpDelete("link")]
    public async Task<IActionResult> Unlink([FromQuery] Guid version, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirst("security_stamp")?.Value, out var stamp)) return Unauthorized();
        await store.UnlinkAsync(currentUser.UserId!.Value, stamp, version, clock.UtcNow, ct);
        return NoContent();
    }

    public sealed record LaunchRequest(string? InitData);
    public sealed record LinkRequest(string? InitData, string? Email, string? Password);
    public sealed record TokenRequest(string? RefreshToken);
}
