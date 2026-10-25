using Finance.Domain.Transactions;

namespace Finance.Application.Transactions.Update;

public sealed record UpdateTransactionCommand(
    Guid TransactionId,
    Guid CategoryId,
    TransactionType Type,
    decimal Amount,
    string? Description,
    DateOnly? OccurredOn,
    Guid? Version = null);
