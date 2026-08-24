using Finance.Domain.Categories;
using Finance.Domain.Transactions;

namespace Finance.UnitTests.Domain;

public sealed class CategoryTests
{
    [Fact]
    public void Activate_AfterDeactivation_RestoresCategory()
    {
        var category = Category.Create(
            Guid.NewGuid(),
            "Food",
            TransactionType.Expense);

        category.Deactivate();
        category.Activate();

        Assert.True(category.IsActive);
    }
}
