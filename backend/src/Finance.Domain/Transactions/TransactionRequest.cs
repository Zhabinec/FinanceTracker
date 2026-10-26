using Finance.Domain.Common;

namespace Finance.Domain.Transactions;

// Kept after transaction deletion so a delayed request cannot recreate a deleted expense.
public sealed class TransactionRequest
{
    private TransactionRequest() { }
    public Guid UserId { get; private set; }
    public Guid RequestId { get; private set; }
    public Guid TransactionId { get; private set; }
    public string Fingerprint { get; private set; } = "";
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public static TransactionRequest Create(Guid userId, Guid requestId, Guid transactionId, string fingerprint, DateTimeOffset createdAtUtc)
    {
        if (userId == Guid.Empty || requestId == Guid.Empty || transactionId == Guid.Empty ||
            fingerprint.Length != 64 || !fingerprint.All(Uri.IsHexDigit) || createdAtUtc.Offset != TimeSpan.Zero)
            throw new DomainException("Некорректный идентификатор запроса операции.");
        return new() { UserId = userId, RequestId = requestId, TransactionId = transactionId, Fingerprint = fingerprint, CreatedAtUtc = createdAtUtc };
    }
}
