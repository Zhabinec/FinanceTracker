using Finance.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using FinanceTransaction = Finance.Domain.Transactions.Transaction;

namespace Finance.Infrastructure.Persistence;

public sealed class TransactionStore(FinanceDbContext dbContext) : ITransactionStore
{
    public async Task<IReadOnlyList<TransactionRecord>> ListByUserAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        return await (
                from transaction in dbContext.Transactions.AsNoTracking()
                join category in dbContext.Categories.AsNoTracking()
                    on transaction.CategoryId equals category.Id
                where transaction.UserId == userId
                orderby transaction.OccurredAtUtc descending,
                    transaction.CreatedAtUtc descending
                select new TransactionRecord(transaction, category.Name))
            .ToListAsync(cancellationToken);
    }

    public Task<FinanceTransaction?> FindOwnedAsync(
        Guid transactionId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        return dbContext.Transactions.SingleOrDefaultAsync(
            transaction => transaction.Id == transactionId &&
                transaction.UserId == userId,
            cancellationToken);
    }

    public void Add(FinanceTransaction transaction) =>
        dbContext.Transactions.Add(transaction);

    public void Remove(FinanceTransaction transaction) =>
        dbContext.Transactions.Remove(transaction);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
