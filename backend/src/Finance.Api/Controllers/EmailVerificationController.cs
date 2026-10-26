using Finance.Application.Auth.Verification;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Finance.Api.Controllers;

[ApiController]
[Route("api/auth")]
[EnableRateLimiting("password-recovery")]
[RequestSizeLimit(16_384)]
public sealed class EmailVerificationController(IEmailVerification handler,
    Finance.Infrastructure.Security.RecoveryEmailOptions options, IConfiguration configuration) : ControllerBase
{
    public sealed record RequestEmail(string? Email);
    public sealed record ConfirmEmail(string? Token);

    [HttpGet("email-delivery")]
    [DisableRateLimiting]
    public IActionResult DeliveryMode() => Ok(new
    {
        simulated = options.Provider == "Mailpit",
        previewUrl = options.Provider == "Mailpit" ? configuration["Email:PreviewUrl"] ?? "http://localhost:8025" : null
    });

    [HttpPost("resend-confirmation")]
    public async Task<IActionResult> Resend(RequestEmail request, CancellationToken cancellationToken)
    {
        await handler.RequestAsync(request.Email, cancellationToken);
        return Accepted(new { message = "Если аккаунт существует и email ещё не подтверждён, письмо будет отправлено." });
    }

    [HttpPost("confirm-email")]
    public async Task<IActionResult> Confirm(ConfirmEmail request, CancellationToken cancellationToken) =>
        await handler.ConfirmAsync(request.Token, cancellationToken) ? NoContent() :
            BadRequest(new ProblemDetails { Status = 400, Title = "Ссылка подтверждения истекла, уже использована или недействительна. Запросите новое письмо." });
}
