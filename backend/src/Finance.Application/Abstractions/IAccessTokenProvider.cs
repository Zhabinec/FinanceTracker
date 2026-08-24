using Finance.Domain.Users;

namespace Finance.Application.Abstractions;

public interface IAccessTokenProvider
{
    AccessToken Create(User user);
}

public sealed record AccessToken(string Value, DateTimeOffset ExpiresAtUtc);
