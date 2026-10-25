namespace Finance.Domain.Users;

public sealed class EmailConfirmationToken
{
    private EmailConfirmationToken() { }
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = "";
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? ConsumedAtUtc { get; private set; }

    public static EmailConfirmationToken Create(Guid userId, string tokenHash, DateTimeOffset expiresAtUtc) => new()
    {
        Id = Guid.NewGuid(), UserId = userId, TokenHash = tokenHash, ExpiresAtUtc = expiresAtUtc
    };

    public bool IsUsable(DateTimeOffset nowUtc) => ConsumedAtUtc is null && ExpiresAtUtc > nowUtc;
    public void Consume(DateTimeOffset nowUtc) => ConsumedAtUtc = nowUtc;
}
