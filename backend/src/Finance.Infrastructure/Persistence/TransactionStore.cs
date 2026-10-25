using Finance.Application.Abstractions;
using Finance.Application.Transactions;
using Finance.Application.Transactions.GetTransactions;
using Microsoft.EntityFrameworkCore;
using FinanceTransaction = Finance.Domain.Transactions.Transaction;

namespace Finance.Infrastructure.Persistence;

public sealed class TransactionStore(FinanceDbContext dbContext) : ITransactionStore
{
    public async Task<TransactionRecordPage> ListByUserAsync(
        Guid userId,
        TransactionQuery query,
        CancellationToken cancellationToken)
    {
        query.Validate();
        var transactions = dbContext.Transactions.AsNoTracking().Where(item => item.UserId == userId);
        if (query.From is { } from)
        {
            var start = MoscowDate.ToUtc(from);
            transactions = transactions.Where(item => item.OccurredAtUtc >= start);
        }
        if (query.To is { } to)
        {
            var end = MoscowDate.ToUtc(to.AddDays(1));
            transactions = transactions.Where(item => item.OccurredAtUtc < end);
        }
        if (query.CategoryId is { } categoryId) transactions = transactions.Where(item => item.CategoryId == categoryId);
        if (query.Type is { } type) transactions = transactions.Where(item => item.Type == type);
        var total = await transactions.CountAsync(cancellationToken);
        var ordered = query.Sort switch
        {
            "dateAsc" => transactions.OrderBy(item => item.OccurredAtUtc),
            "amountAsc" => transactions.OrderBy(item => item.Amount),
            "amountDesc" => transactions.OrderByDescending(item => item.Amount),
            _ => transactions.OrderByDescending(item => item.OccurredAtUtc)
        };
        // A unique tie-breaker keeps page boundaries deterministic for equal dates/amounts.
        var page = ordered.ThenByDescending(item => item.CreatedAtUtc).ThenBy(item => item.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize);
        var items = await (
                from transaction in page
                join category in dbContext.Categories.AsNoTracking()
                    on transaction.CategoryId equals category.Id
                select new TransactionRecord(transaction, category.Name))
            .ToListAsync(cancellationToken);
        return new TransactionRecordPage(items, total);
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
