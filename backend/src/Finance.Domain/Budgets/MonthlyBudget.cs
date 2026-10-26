using Finance.Domain.Categories;
using Finance.Domain.Common;
using Finance.Domain.Transactions;

namespace Finance.Domain.Budgets;

public sealed class MonthlyBudget
{
    private MonthlyBudget() { }
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public Guid CategoryId { get; private set; }
    public TransactionType Type { get; private set; } = TransactionType.Expense;
    public DateOnly Month { get; private set; }
    public decimal Limit { get; private set; }
    public Guid Version { get; private set; }

    public static MonthlyBudget Create(Guid userId, Category category, DateOnly month, decimal limit)
    {
        if (userId == Guid.Empty || category.UserId != userId)
            throw new DomainException("Категория должна принадлежать владельцу бюджета.");
        if (category.Type != TransactionType.Expense)
            throw new DomainException("Бюджет доступен только для расходной категории.");
        ValidateMonth(month);
        var budget = new MonthlyBudget { Id = Guid.NewGuid(), UserId = userId, CategoryId = category.Id, Month = month };
        budget.ChangeLimit(limit);
        return budget;
    }

    public static void ValidateMonth(DateOnly month)
    {
        if (month.Day != 1 || month.Year is < 1900 or > 9998)
            throw new DomainException("Укажите первый день месяца с 1900 по 9998 год.");
    }

    public void ChangeLimit(decimal limit)
    {
        if (limit <= 0 || limit > Transaction.MaxAmount || decimal.Round(limit, 2) != limit)
            throw new DomainException("Лимит должен быть положительной суммой не больше 9999999999999999.99 с двумя знаками после запятой.");
        Limit = limit;
        Version = Guid.NewGuid();
    }
}
