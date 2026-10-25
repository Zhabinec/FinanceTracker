using Finance.Domain.Transactions;

namespace Finance.Application.Transactions.Create;

public sealed record CreateTransactionCommand(
    Guid CategoryId,
    TransactionType Type,
    decimal Amount,
    string? Description,
    DateOnly? OccurredOn,
    Guid? AccountId = null);
