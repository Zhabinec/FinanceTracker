using System.Globalization;
using Finance.Application.Budgets;
using Finance.Application.Common;
using Finance.Application.Transactions;
using Finance.Domain.Budgets;
using Finance.Domain.Transactions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Finance.Infrastructure.Persistence;

public sealed class BudgetStore(FinanceDbContext db) : IBudgetStore
{
    public async Task<IReadOnlyList<BudgetDto>> ListAsync(Guid userId, DateOnly month, CancellationToken cancellationToken)
    {
        var fromUtc = MoscowDate.ToUtc(month);
        var toUtc = MoscowDate.ToUtc(month.AddMonths(1));
        var rows = await (from budget in db.MonthlyBudgets.AsNoTracking()
                          where budget.UserId == userId && budget.Month == month
                          join category in db.Categories on budget.CategoryId equals category.Id
                          orderby category.Name, budget.Id
                          select new { Budget = budget, category.Name, category.IsActive,
                              Spent = db.Transactions.Where(t => t.UserId == userId && t.CategoryId == budget.CategoryId &&
                                  t.Type == TransactionType.Expense && t.OccurredAtUtc >= fromUtc && t.OccurredAtUtc < toUtc)
                                  .Sum(t => (decimal?)t.Amount) ?? 0m }).ToListAsync(cancellationToken);
        return rows.Select(row => new BudgetDto(row.Budget.Id, row.Budget.CategoryId, row.Name, row.IsActive, month,
            Money(row.Budget.Limit), Money(row.Spent), Money(Math.Max(0m, row.Budget.Limit - row.Spent)),
            Money(Math.Max(0m, row.Spent - row.Budget.Limit)), row.Budget.Version)).ToArray();
    }
    private static string Money(decimal value) => value.ToString("F2", CultureInfo.InvariantCulture);
    public Task<MonthlyBudget?> FindOwnedAsync(Guid userId, Guid id, CancellationToken cancellationToken) =>
        db.MonthlyBudgets.SingleOrDefaultAsync(b => b.Id == id && b.UserId == userId, cancellationToken);
    public void Add(MonthlyBudget budget) => db.MonthlyBudgets.Add(budget);
    public void Remove(MonthlyBudget budget) => db.MonthlyBudgets.Remove(budget);
    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { throw new ConflictException("Бюджет уже изменён. Обновите список."); }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_monthly_budgets_UserId_Month_CategoryId" })
        { throw new ConflictException("Для этой категории уже задан лимит на выбранный месяц."); }
    }
}
