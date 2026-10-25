using Finance.Domain.Common;

namespace Finance.Domain.Users;

public sealed class RefreshSession
{
    private RefreshSession() { }

    private RefreshSession(Guid id, Guid userId, string tokenHash,
        DateTimeOffset createdAtUtc, DateTimeOffset expiresAtUtc)
    {
        Id = id;
        UserId = userId;
        TokenHash = tokenHash;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public Guid SecurityStamp { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }

    public static RefreshSession Create(Guid userId, string tokenHash,
        DateTimeOffset createdAtUtc, DateTimeOffset expiresAtUtc, Guid securityStamp = default)
    {
        if (userId == Guid.Empty) throw new DomainException("User id is required.");
        if (tokenHash.Length != 64 || tokenHash.Any(character => !Uri.IsHexDigit(character)))
            throw new DomainException("Token hash must be a SHA-256 hexadecimal value.");
        if (createdAtUtc.Offset != TimeSpan.Zero || expiresAtUtc.Offset != TimeSpan.Zero)
            throw new DomainException("Refresh session dates must be in UTC.");
        if (expiresAtUtc <= createdAtUtc)
            throw new DomainException("Refresh session must expire after it is created.");

        return new RefreshSession(Guid.NewGuid(), userId, tokenHash, createdAtUtc, expiresAtUtc) { SecurityStamp = securityStamp };
    }

    public void Revoke(DateTimeOffset revokedAtUtc)
    {
        if (revokedAtUtc.Offset != TimeSpan.Zero)
            throw new DomainException("Revocation date must be in UTC.");

        RevokedAtUtc ??= revokedAtUtc;
    }
}
