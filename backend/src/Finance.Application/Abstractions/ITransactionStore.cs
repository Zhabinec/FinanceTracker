using Finance.Domain.Transactions;
using FinanceTransaction = Finance.Domain.Transactions.Transaction;

namespace Finance.Application.Abstractions;

public sealed record TransactionRecord(
    FinanceTransaction Transaction,
    string CategoryName);

public interface ITransactionStore
{
    Task<IReadOnlyList<TransactionRecord>> ListByUserAsync(
        Guid userId,
        CancellationToken cancellationToken);

    Task<FinanceTransaction?> FindOwnedAsync(
        Guid transactionId,
        Guid userId,
        CancellationToken cancellationToken);

    void Add(FinanceTransaction transaction);

    void Remove(FinanceTransaction transaction);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
