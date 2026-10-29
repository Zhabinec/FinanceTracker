using Finance.Domain.Common;

namespace Finance.Domain.Ledger;

public enum FinancialRecordKind { Account = 1, Category = 2, Transaction = 3, Transfer = 4, Budget = 5, Request = 6, Import = 7, DailyTotal = 8 }

// Only access/index metadata is public to storage. Names, amounts and descriptions live in the payload.
public sealed class FinancialRecord
{
    private FinancialRecord() { }
    public Guid UserId { get; private set; }
    public FinancialRecordKind Kind { get; private set; }
    public Guid Id { get; private set; }
    public Guid Version { get; private set; }
    public DateOnly? OccurredOn { get; private set; }
    public Guid? AccountId { get; private set; }
    public Guid? CategoryId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public byte[] Nonce { get; private set; } = [];
    public byte[] Ciphertext { get; private set; } = [];

    public static FinancialRecord Create(Guid userId, FinancialRecordKind kind, Guid id, Guid version,
        DateTimeOffset createdAtUtc, DateOnly? occurredOn = null, Guid? accountId = null, Guid? categoryId = null)
    {
        if (userId == Guid.Empty || id == Guid.Empty || version == Guid.Empty || !Enum.IsDefined(kind) ||
            createdAtUtc.Offset != TimeSpan.Zero || occurredOn?.Year is < 1900 or > 9998 ||
            accountId == Guid.Empty || categoryId == Guid.Empty) throw new DomainException("Некорректная финансовая запись.");
        // PostgreSQL timestamps have microsecond precision; authenticate the persisted value exactly.
        createdAtUtc = CanonicalTimestamp(createdAtUtc);
        return new() { UserId = userId, Kind = kind, Id = id, Version = version, CreatedAtUtc = createdAtUtc,
            OccurredOn = occurredOn, AccountId = accountId, CategoryId = categoryId };
    }

    public static DateTimeOffset CanonicalTimestamp(DateTimeOffset value)
    {
        if (value.Offset != TimeSpan.Zero) throw new DomainException("Время финансовой записи должно быть в UTC.");
        return new DateTimeOffset(value.Ticks - value.Ticks % 10, TimeSpan.Zero);
    }

    public void Revise(Guid version, DateOnly? day = null, Guid? accountId = null, Guid? categoryId = null)
    {
        if (version == Guid.Empty || version == Version || day?.Year is < 1900 or > 9998 ||
            accountId == Guid.Empty || categoryId == Guid.Empty) throw new DomainException("Некорректная версия записи.");
        Version = version; OccurredOn = day; AccountId = accountId; CategoryId = categoryId;
    }

    public void Protect(byte[] nonce, byte[] ciphertext)
    {
        if (nonce.Length != 12 || ciphertext.Length is < 17 or > 16_384 || nonce.AsSpan().SequenceEqual(Nonce))
            throw new DomainException("Некорректная защита финансовой записи.");
        Nonce = nonce.ToArray(); Ciphertext = ciphertext.ToArray();
    }
}
