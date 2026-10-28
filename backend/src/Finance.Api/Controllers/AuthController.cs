using Finance.Api.Contracts.Auth;
using Finance.Application.Auth.Login;
using Finance.Application.Auth.Register;
using Finance.Application.Auth.Refresh;
using Finance.Application.Auth.Logout;
using Finance.Api.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Finance.Api.Controllers;

[ApiController]
[Route("api/auth")]
[RequestSizeLimit(16_384)]
public sealed class AuthController(
    RegisterUserHandler registerUserHandler,
    LoginUserHandler loginUserHandler,
    RefreshTokensHandler refreshTokensHandler,
    LogoutHandler logoutHandler,
    RefreshCookieService refreshCookieService,
    IConfiguration configuration,
    Finance.Infrastructure.Security.RecoveryEmailOptions emailOptions) : ControllerBase
{
    private bool EmailRegistrationEnabled => configuration.GetValue("Auth:EmailRegistrationEnabled",
        emailOptions.Provider == "Mailpit");

    [HttpGet("options")]
    public IActionResult Options() => Ok(new { emailRegistrationEnabled = EmailRegistrationEnabled });

    [HttpPost("register")]
    [EnableRateLimiting("password-recovery")]
    [ProducesResponseType<RegisteredUserDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register(
        RegisterUserRequest request,
        CancellationToken cancellationToken)
    {
        if (!EmailRegistrationEnabled)
            return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Регистрация по email отключена. Используйте Telegram или Google."
            });

        var command = new RegisterUserCommand(
            request.Email,
            request.Password,
            request.BaseCurrency);

        var result = await registerUserHandler.HandleAsync(command, cancellationToken);

        return result.Status switch
        {
            RegisterUserStatus.Success => StatusCode(StatusCodes.Status201Created, result.User),
            RegisterUserStatus.InvalidInput => BadRequest(new ValidationProblemDetails(
                result.Errors.ToDictionary(error => error.Key, error => error.Value))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred."
            }),
            RegisterUserStatus.EmailAlreadyExists => Conflict(new ProblemDetails
            {
                Title = "Email is already registered.",
                Status = StatusCodes.Status409Conflict
            }),
            _ => throw new InvalidOperationException("Unknown registration result.")
        };
    }

    [HttpPost("login")]
    [EnableRateLimiting("password-login")]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        var result = await loginUserHandler.HandleAsync(
            new LoginUserCommand(request.Email, request.Password),
            cancellationToken);

        if (result.Status == LoginUserStatus.InvalidCredentials)
        {
            return Unauthorized(new ProblemDetails
            {
                Title = "Invalid email or password.",
                Status = StatusCodes.Status401Unauthorized
            });
        }

        if (result.Status == LoginUserStatus.EmailNotConfirmed)
            return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
            { Status = 403, Title = "Подтвердите email по ссылке из письма перед входом." });

        refreshCookieService.Write(Response, result.RefreshToken!,
            result.RefreshTokenExpiresAtUtc!.Value);
        return Ok(new LoginResponse(result.AccessToken!, result.ExpiresAtUtc!.Value));
    }

    [HttpPost("refresh")]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken)
    {
        var token = refreshCookieService.Read(Request);
        var result = await refreshTokensHandler.HandleAsync(
            new RefreshTokensCommand(token ?? string.Empty), cancellationToken);

        if (result.Status == RefreshTokensStatus.InvalidToken)
        {
            refreshCookieService.Delete(Response);
            return Unauthorized(new ProblemDetails
            {
                Title = "Invalid refresh token.",
                Status = StatusCodes.Status401Unauthorized
            });
        }

        refreshCookieService.Write(Response, result.RefreshToken!,
            result.RefreshTokenExpiresAtUtc!.Value);
        return Ok(new LoginResponse(result.AccessToken!,
            result.AccessTokenExpiresAtUtc!.Value));
    }

    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        await logoutHandler.HandleAsync(
            new LogoutCommand(refreshCookieService.Read(Request) ?? string.Empty),
            cancellationToken);
        refreshCookieService.Delete(Response);
        return NoContent();
    }
}
