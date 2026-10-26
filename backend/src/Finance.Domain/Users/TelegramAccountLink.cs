using Finance.Domain.Common;

namespace Finance.Domain.Users;

public sealed class TelegramAccountLink
{
    private TelegramAccountLink() { }
    public Guid UserId { get; private set; }
    public long TelegramUserId { get; private set; }
    public Guid Version { get; private set; }
    public DateTimeOffset LinkedAtUtc { get; private set; }

    public static TelegramAccountLink Create(Guid userId, long telegramUserId, DateTimeOffset nowUtc)
    {
        if (userId == Guid.Empty || telegramUserId is <= 0 or > 4_503_599_627_370_495 || nowUtc.Offset != TimeSpan.Zero)
            throw new DomainException("Некорректная привязка Telegram.");
        return new() { UserId = userId, TelegramUserId = telegramUserId, Version = Guid.NewGuid(), LinkedAtUtc = nowUtc };
    }
}
