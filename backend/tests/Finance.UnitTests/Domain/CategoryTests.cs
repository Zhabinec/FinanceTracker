using Finance.Domain.Categories;
using Finance.Domain.Transactions;

namespace Finance.UnitTests.Domain;

public sealed class CategoryTests
{
    [Fact]
    public void Create_WithValidData_TrimsNameAndActivatesCategory()
    {
        var userId = Guid.NewGuid();

        var category = Category.Create(
            userId,
            "  Food  ",
            TransactionType.Expense);

        Assert.Equal(userId, category.UserId);
        Assert.Equal("Food", category.Name);
        Assert.Equal(TransactionType.Expense, category.Type);
        Assert.True(category.IsActive);
    }

    [Fact]
    public void Rename_WithWhitespaceOnlyName_Throws()
    {
        var category = Category.Create(
            Guid.NewGuid(),
            "Food",
            TransactionType.Expense);

        Assert.Throws<Finance.Domain.Common.DomainException>(() => category.Rename("   "));
    }

    [Fact]
    public void Deactivate_ActiveCategory_MarksItInactive()
    {
        var category = Category.Create(
            Guid.NewGuid(),
            "Food",
            TransactionType.Expense);

        category.Deactivate();

        Assert.False(category.IsActive);
    }

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
