namespace Finance.Domain.Users;

public sealed class PasswordResetToken
{
    private PasswordResetToken() { }
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public Guid SecurityStamp { get; private set; }
    public string TokenHash { get; private set; } = "";
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? ConsumedAtUtc { get; private set; }

    public static PasswordResetToken Create(User user, string hash, DateTimeOffset expiresAtUtc) => new()
    {
        Id = Guid.NewGuid(), UserId = user.Id, SecurityStamp = user.SecurityStamp,
        TokenHash = hash, ExpiresAtUtc = expiresAtUtc
    };

    public bool CanConsume(User user, DateTimeOffset nowUtc) =>
        ConsumedAtUtc is null && ExpiresAtUtc > nowUtc && SecurityStamp == user.SecurityStamp && UserId == user.Id;

    public void Consume(DateTimeOffset nowUtc) => ConsumedAtUtc = nowUtc;
}
