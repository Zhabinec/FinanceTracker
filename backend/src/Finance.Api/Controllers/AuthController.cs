using Finance.Api.Contracts.Auth;
using Finance.Application.Auth.Login;
using Finance.Application.Auth.Register;
using Microsoft.AspNetCore.Mvc;

namespace Finance.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    RegisterUserHandler registerUserHandler,
    LoginUserHandler loginUserHandler) : ControllerBase
{
    [HttpPost("register")]
    [ProducesResponseType<RegisteredUserDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register(
        RegisterUserRequest request,
        CancellationToken cancellationToken)
    {
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

        return Ok(new LoginResponse(result.AccessToken!, result.ExpiresAtUtc!.Value));
    }
}
