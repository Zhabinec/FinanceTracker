namespace Finance.Application.Auth.Register;

public sealed record RegisteredUserDto(
    Guid Id,
    string Email,
    string BaseCurrency,
    DateTimeOffset RegisteredAtUtc);
