using Finance.Application.Abstractions;
using Finance.Domain.Transactions;
using FinanceTransaction = Finance.Domain.Transactions.Transaction;

namespace Finance.Application.Transactions;

public sealed record TransactionDto(
    Guid Id,
    Guid CategoryId,
    string CategoryName,
    TransactionType Type,
    decimal Amount,
    string? Description,
    DateOnly OccurredOn,
    DateTimeOffset CreatedAtUtc)
{
    public static TransactionDto From(
        FinanceTransaction transaction,
        string categoryName) =>
        new(
            transaction.Id,
            transaction.CategoryId,
            categoryName,
            transaction.Type,
            transaction.Amount,
            transaction.Description,
            MoscowDate.FromUtc(transaction.OccurredAtUtc),
            transaction.CreatedAtUtc);

    public static TransactionDto From(TransactionRecord record) =>
        From(record.Transaction, record.CategoryName);
}
