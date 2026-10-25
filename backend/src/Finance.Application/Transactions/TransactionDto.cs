using System.Globalization;
using Finance.Application.Abstractions;
using Finance.Domain.Transactions;
using FinanceTransaction = Finance.Domain.Transactions.Transaction;

namespace Finance.Application.Transactions;

public sealed record TransactionDto(
    Guid Id,
    Guid CategoryId,
    string CategoryName,
    TransactionType Type,
    string Amount,
    string? Description,
    DateOnly OccurredOn,
    DateTimeOffset CreatedAtUtc,
    Guid Version)
{
    public static TransactionDto From(
        FinanceTransaction transaction,
        string categoryName) =>
        new(
            transaction.Id,
            transaction.CategoryId,
            categoryName,
            transaction.Type,
            transaction.Amount.ToString("F2", CultureInfo.InvariantCulture),
            transaction.Description,
            MoscowDate.FromUtc(transaction.OccurredAtUtc),
            transaction.CreatedAtUtc,
            transaction.Version);

    public static TransactionDto From(TransactionRecord record) =>
        From(record.Transaction, record.CategoryName);
}
