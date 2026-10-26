using Finance.Application.Auth.Recovery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Finance.Api.Controllers;

[ApiController]
[Route("api/auth")]
[EnableRateLimiting("password-recovery")]
[RequestSizeLimit(16_384)]
public sealed class PasswordRecoveryController(PasswordRecoveryHandler handler, ILogger<PasswordRecoveryController> logger) : ControllerBase
{
    public sealed record ForgotRequest(string? Email);
    public sealed record ResetRequest(string? Token, string? Password);

    [HttpPost("forgot-password")]
    public async Task<IActionResult> Forgot(ForgotRequest request, CancellationToken cancellationToken)
    {
        try { await handler.RequestAsync(request.Email, cancellationToken); }
        catch (HttpRequestException) { logger.LogError("Recovery email delivery failed. Trace {TraceId}", HttpContext.TraceIdentifier); }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        { logger.LogError("Recovery email delivery timed out. Trace {TraceId}", HttpContext.TraceIdentifier); }
        return Accepted(new { message = "Если аккаунт существует, письмо с инструкцией будет отправлено." });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> Reset(ResetRequest request, CancellationToken cancellationToken)
    {
        if (!await handler.ResetAsync(request.Token, request.Password, cancellationToken))
            return BadRequest(new ProblemDetails { Status = 400, Title = "Ссылка недействительна или срок её действия истёк." });
        return NoContent();
    }
}
