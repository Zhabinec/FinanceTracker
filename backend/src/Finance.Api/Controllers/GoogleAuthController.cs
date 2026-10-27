using System.Security.Claims;
using System.Text.Json;
using Finance.Api.Security;
using Finance.Application.Abstractions;
using Finance.Application.Auth.Google;
using Finance.Application.Common;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Finance.Api.Controllers;

[ApiController]
[Route("api/auth/google")]
[EnableRateLimiting("password-login")]
public sealed class GoogleAuthController(GoogleSettings settings, GoogleAuthHandler handler,
    IDataProtectionProvider protection, ICurrentUser currentUser, IClock clock, RefreshCookieService cookies) : ControllerBase
{
    private const string LinkCookie = "finance.google.link";
    private sealed record LinkIntent(Guid UserId, Guid SecurityStamp, DateTimeOffset Expires);
    private CookieOptions IntentCookie => new() { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict,
        Path = "/api/auth/google", MaxAge = TimeSpan.FromMinutes(5), IsEssential = true };
    [HttpGet("options")]
    public IActionResult Options() => Ok(new { settings.Enabled });

    [Authorize]
    [HttpPost("link/start")]
    public IActionResult LinkStart()
    {
        if (!settings.Enabled) return NotFound();
        if (!Guid.TryParse(User.FindFirstValue("security_stamp"), out var stamp)) return Unauthorized();
        var intent = new LinkIntent(currentUser.UserId!.Value, stamp, clock.UtcNow.AddMinutes(5));
        Response.Cookies.Append(LinkCookie, protection.CreateProtector("FinanceTracker.GoogleLink.v1").Protect(JsonSerializer.Serialize(intent)), IntentCookie);
        return Ok(new { url = "/api/auth/google/start?link=true" });
    }

    [HttpGet("start")]
    public IActionResult Start([FromQuery] bool link = false)
    {
        if (!settings.Enabled) return NotFound();
        var properties = new AuthenticationProperties { RedirectUri = "/api/auth/google/complete", IsPersistent = false };
        if (link)
        {
            try
            {
                var encrypted = Request.Cookies[LinkCookie] ?? throw new UnauthorizedAccessException();
                var intent = JsonSerializer.Deserialize<LinkIntent>(protection.CreateProtector("FinanceTracker.GoogleLink.v1").Unprotect(encrypted));
                Response.Cookies.Delete(LinkCookie, IntentCookie);
                if (intent is null || intent.Expires <= clock.UtcNow) return Unauthorized();
                properties.Items["link_user"] = intent.UserId.ToString();
                properties.Items["link_stamp"] = intent.SecurityStamp.ToString();
            }
            catch (Exception e) when (e is System.Security.Cryptography.CryptographicException or JsonException or UnauthorizedAccessException)
            { return Unauthorized(); }
        }
        return Challenge(properties, GoogleAuthentication.Scheme);
    }

    [HttpGet("complete")]
    public async Task<IActionResult> Complete(CancellationToken ct)
    {
        if (!settings.Enabled) return NotFound();
        var external = await HttpContext.AuthenticateAsync(GoogleAuthentication.ExternalCookie);
        await HttpContext.SignOutAsync(GoogleAuthentication.ExternalCookie);
        if (!external.Succeeded || external.Principal is null) return Redirect("/login?google=failed");
        GoogleLinkOwner? link = null;
        var items = external.Properties?.Items;
        if (items?.ContainsKey("link_user") == true)
        {
            if (!Guid.TryParse(items["link_user"], out var user) || !items.TryGetValue("link_stamp", out var rawStamp) || !Guid.TryParse(rawStamp, out var stamp))
                return Redirect("/login?google=failed");
            link = new(user, stamp);
        }
        var principal = external.Principal;
        var proof = new GoogleProof(principal.FindFirstValue("sub") ?? "", principal.FindFirstValue("email") ?? "",
            string.Equals(principal.FindFirstValue("email_verified"), "true", StringComparison.OrdinalIgnoreCase));
        try
        {
            var session = await handler.LoginAsync(proof, link, ct);
            cookies.Write(Response, session.RefreshToken, session.ExpiresAtUtc);
            return Redirect(link is null ? "/" : "/account?google=linked");
        }
        catch (ConflictException) { return Redirect("/login?google=link_required"); }
        catch (UnauthorizedAccessException) { return Redirect("/login?google=failed"); }
    }
}
