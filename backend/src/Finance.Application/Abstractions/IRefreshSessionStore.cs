using Finance.Domain.Users;

namespace Finance.Application.Abstractions;

public interface IRefreshSessionStore
{
    Task AddAsync(RefreshSession session, CancellationToken cancellationToken);

    Task<User?> RotateAsync(string currentTokenHash, string newTokenHash,
        DateTimeOffset nowUtc, DateTimeOffset newExpiresAtUtc,
        CancellationToken cancellationToken);

    Task RevokeAsync(string tokenHash, DateTimeOffset nowUtc,
        CancellationToken cancellationToken);
}
