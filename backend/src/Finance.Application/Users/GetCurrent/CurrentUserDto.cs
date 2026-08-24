namespace Finance.Application.Users.GetCurrent;

public sealed record CurrentUserDto(
    Guid Id,
    string Email,
    string BaseCurrency,
    DateTimeOffset RegisteredAtUtc);
