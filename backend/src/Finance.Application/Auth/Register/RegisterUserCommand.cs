namespace Finance.Application.Auth.Register;

public sealed record RegisterUserCommand(
    string Email,
    string Password,
    string BaseCurrency);
