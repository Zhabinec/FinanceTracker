using System.Data;
using Finance.Application.Abstractions;
using Finance.Application.Dashboard;
using Microsoft.EntityFrameworkCore;

namespace Finance.Infrastructure.Persistence;

public sealed class DashboardStore(FinanceDbContext dbContext) : IDashboardStore
{
    public async Task<DashboardSnapshot> ReadAsync(Guid userId, DateTimeOffset fromUtc,
        DateTimeOffset toExclusiveUtc, CancellationToken cancellationToken)
    {
        await using var snapshot = await dbContext.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var transactions = dbContext.Transactions.AsNoTracking().Where(item => item.UserId == userId &&
            item.OccurredAtUtc >= fromUtc && item.OccurredAtUtc < toExclusiveUtc);
        var categories = await (
            from item in transactions
            join category in dbContext.Categories.AsNoTracking() on item.CategoryId equals category.Id
            group item by new { item.CategoryId, category.Name, item.Type } into items
            select new CategoryTotal(items.Key.CategoryId, items.Key.Name, items.Key.Type, items.Sum(item => item.Amount)))
            .ToListAsync(cancellationToken);
        var recent = await (
            from item in transactions.OrderByDescending(item => item.OccurredAtUtc)
                .ThenByDescending(item => item.CreatedAtUtc).ThenBy(item => item.Id).Take(5)
            join category in dbContext.Categories.AsNoTracking() on item.CategoryId equals category.Id
            select new TransactionRecord(item, category.Name)).ToListAsync(cancellationToken);
        await snapshot.CommitAsync(cancellationToken);
        return new DashboardSnapshot(categories.OrderByDescending(item => item.Amount).ThenBy(item => item.CategoryId).ToArray(), recent);
    }
}
