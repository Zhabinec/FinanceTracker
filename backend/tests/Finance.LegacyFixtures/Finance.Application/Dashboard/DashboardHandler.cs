using System.Globalization;
using Finance.Application.Abstractions;
using Finance.Application.Transactions;
using Finance.Application.Transactions.GetTransactions;
using Finance.Domain.Transactions;

namespace Finance.Application.Dashboard;

public sealed record CategoryTotal(Guid CategoryId, string CategoryName, TransactionType Type, decimal Amount);
public sealed record DashboardSnapshot(IReadOnlyList<CategoryTotal> Categories, IReadOnlyList<TransactionRecord> Recent);
public interface IDashboardStore
{
    Task<DashboardSnapshot> ReadAsync(Guid userId, DateTimeOffset fromUtc, DateTimeOffset toExclusiveUtc,
        CancellationToken cancellationToken);
}
public sealed class DashboardHandler(ICurrentUser currentUser, IDashboardStore store, IClock clock)
{
    public async Task<DashboardDto?> HandleAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId) return null;
        var today = MoscowDate.FromUtc(clock.UtcNow);
        from ??= new DateOnly(today.Year, today.Month, 1);
        to ??= today;
        new TransactionQuery(From: from, To: to).Validate();
        var snapshot = await store.ReadAsync(userId, MoscowDate.ToUtc(from.Value),
            MoscowDate.ToUtc(to.Value.AddDays(1)), cancellationToken);
        var income = snapshot.Categories.Where(item => item.Type == TransactionType.Income).Sum(item => item.Amount);
        var expense = snapshot.Categories.Where(item => item.Type == TransactionType.Expense).Sum(item => item.Amount);
        return new DashboardDto(from.Value, to.Value, Money(income), Money(expense), Money(income - expense),
            snapshot.Categories.Select(item => new CategoryTotalDto(item.CategoryId, item.CategoryName, item.Type, Money(item.Amount))).ToArray(),
            snapshot.Recent.Select(TransactionDto.From).ToArray());
    }

    private static string Money(decimal amount) => amount.ToString("F2", CultureInfo.InvariantCulture);
}
