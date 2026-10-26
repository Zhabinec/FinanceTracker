using Finance.Domain.Budgets;
using Finance.Domain.Categories;
using Finance.Domain.Common;
using Finance.Domain.Transactions;

namespace Finance.UnitTests.Domain;

public sealed class MonthlyBudgetTests
{
    [Fact]
    public void Budget_RequiresOwnedExpenseCategoryAndWholeMonth()
    {
        var user = Guid.NewGuid();
        var category = Category.Create(user, "Продукты", TransactionType.Expense);
        Assert.Throws<DomainException>(() => MonthlyBudget.Create(Guid.NewGuid(), category, new(2026, 10, 1), 1));
        Assert.Throws<DomainException>(() => MonthlyBudget.Create(user, Category.Create(user, "Зарплата", TransactionType.Income), new(2026, 10, 1), 1));
        Assert.Throws<DomainException>(() => MonthlyBudget.Create(user, category, new(2026, 10, 2), 1));
        Assert.Throws<DomainException>(() => MonthlyBudget.Create(user, category, new(9999, 12, 1), 1));
        category.Deactivate();
        var budget = MonthlyBudget.Create(user, category, new(2026, 10, 1), Transaction.MaxAmount);
        var version = budget.Version;
        budget.ChangeLimit(10.01m);
        Assert.NotEqual(version, budget.Version);
        Assert.Equal(10.01m, budget.Limit);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.001")]
    [InlineData("10000000000000000")]
    public void Budget_RejectsInvalidMoney(string value)
    {
        var user = Guid.NewGuid();
        Assert.Throws<DomainException>(() => MonthlyBudget.Create(user,
            Category.Create(user, "Expense", TransactionType.Expense), new(2026, 10, 1),
            decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture)));
    }
}
