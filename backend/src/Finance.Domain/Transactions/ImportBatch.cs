using Finance.Domain.Common;

namespace Finance.Domain.Transactions;

public sealed class ImportBatch
{
    private ImportBatch() { }
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string Fingerprint { get; private set; } = "";
    public int Count { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public static ImportBatch Create(Guid userId, string fingerprint, int count, DateTimeOffset createdAtUtc)
    {
        if (userId == Guid.Empty || fingerprint.Length != 64 || !fingerprint.All(Uri.IsHexDigit) || count is < 1 or > 1000 || createdAtUtc.Offset != TimeSpan.Zero)
            throw new DomainException("Некорректная запись импорта.");
        return new() { Id = Guid.NewGuid(), UserId = userId, Fingerprint = fingerprint, Count = count, CreatedAtUtc = createdAtUtc };
    }
}
