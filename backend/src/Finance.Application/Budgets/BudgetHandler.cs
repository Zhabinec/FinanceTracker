using Finance.Application.Abstractions;
using Finance.Application.Common;
using Finance.Domain.Budgets;

namespace Finance.Application.Budgets;

public sealed record BudgetDto(Guid Id, Guid CategoryId, string CategoryName, bool CategoryActive,
    DateOnly Month, string Limit, string Spent, string Remaining, string Overspent, Guid Version);
public interface IBudgetStore
{
    Task<IReadOnlyList<BudgetDto>> ListAsync(Guid userId, DateOnly month, CancellationToken cancellationToken);
    Task<MonthlyBudget?> FindOwnedAsync(Guid userId, Guid id, CancellationToken cancellationToken);
    void Add(MonthlyBudget budget);
    void Remove(MonthlyBudget budget);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed class BudgetHandler(ICurrentUser currentUser, ICategoryStore categories, IBudgetStore store)
{
    private Guid UserId => currentUser.UserId ?? throw new UnauthorizedAccessException();
    public Task<IReadOnlyList<BudgetDto>> ListAsync(DateOnly month, CancellationToken cancellationToken)
    {
        MonthlyBudget.ValidateMonth(month);
        return store.ListAsync(UserId, month, cancellationToken);
    }

    public async Task<Guid?> CreateAsync(Guid categoryId, DateOnly month, decimal limit, CancellationToken cancellationToken)
    {
        var userId = UserId;
        var category = await categories.FindOwnedAsync(categoryId, userId, cancellationToken);
        if (category is null) return null;
        // Inactive categories retain history and can still have a limit for a past month.
        var budget = MonthlyBudget.Create(userId, category, month, limit);
        store.Add(budget);
        await store.SaveChangesAsync(cancellationToken);
        return budget.Id;
    }

    public async Task<bool> ChangeAsync(Guid id, decimal? limit, Guid version, CancellationToken cancellationToken)
    {
        var budget = await store.FindOwnedAsync(UserId, id, cancellationToken);
        if (budget is null) return false;
        if (version == Guid.Empty || version != budget.Version)
            throw new ConflictException("Бюджет уже изменён. Обновите список и повторите действие.");
        if (limit.HasValue) budget.ChangeLimit(limit.Value);
        else store.Remove(budget);
        await store.SaveChangesAsync(cancellationToken);
        return true;
    }
}
