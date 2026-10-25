using Finance.Domain.Categories;
using Finance.Domain.Common;
using Finance.Domain.Transactions;

namespace Finance.UnitTests.Domain;

public sealed class TransactionTests
{
    private static readonly DateTimeOffset UtcNow = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_WithValidData_CreatesTransaction()
    {
        var userId = Guid.NewGuid();
        var category = Category.Create(userId, "Salary", TransactionType.Income);

        var transaction = Transaction.Create(
            userId,
            category,
            TransactionType.Income,
            10_000.50m,
            " August salary ",
            UtcNow,
            UtcNow);

        Assert.NotEqual(Guid.Empty, transaction.Id);
        Assert.Equal(userId, transaction.UserId);
        Assert.Equal(category.Id, transaction.CategoryId);
        Assert.Equal(10_000.50m, transaction.Amount);
        Assert.Equal("August salary", transaction.Description);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_WithNonPositiveAmount_Throws(decimal amount)
    {
        var userId = Guid.NewGuid();
        var category = Category.Create(userId, "Food", TransactionType.Expense);

        Assert.Throws<DomainException>(() => Transaction.Create(
            userId,
            category,
            TransactionType.Expense,
            amount,
            null,
            UtcNow,
            UtcNow));
    }

    [Fact]
    public void Create_WithAnotherUsersCategory_Throws()
    {
        var category = Category.Create(Guid.NewGuid(), "Food", TransactionType.Expense);

        Assert.Throws<DomainException>(() => Transaction.Create(
            Guid.NewGuid(),
            category,
            TransactionType.Expense,
            100m,
            null,
            UtcNow,
            UtcNow));
    }

    [Fact]
    public void Create_WithMismatchedCategoryType_Throws()
    {
        var userId = Guid.NewGuid();
        var category = Category.Create(userId, "Salary", TransactionType.Income);

        Assert.Throws<DomainException>(() => Transaction.Create(
            userId,
            category,
            TransactionType.Expense,
            100m,
            null,
            UtcNow,
            UtcNow));
    }

    [Fact]
    public void Create_WithInactiveCategory_Throws()
    {
        var userId = Guid.NewGuid();
        var category = Category.Create(userId, "Food", TransactionType.Expense);
        category.Deactivate();

        Assert.Throws<DomainException>(() => Transaction.Create(
            userId,
            category,
            TransactionType.Expense,
            100m,
            null,
            UtcNow,
            UtcNow));
    }

    [Fact]
    public void Create_WithAmountLargerThanDatabasePrecision_Throws()
    {
        var userId = Guid.NewGuid();
        var category = Category.Create(userId, "Salary", TransactionType.Income);

        Assert.Throws<DomainException>(() => Transaction.Create(
            userId,
            category,
            TransactionType.Income,
            Transaction.MaxAmount + 0.01m,
            null,
            UtcNow,
            UtcNow));
    }

    [Fact]
    public void Update_WithSameNowInactiveCategory_UpdatesHistoricalTransaction()
    {
        var userId = Guid.NewGuid();
        var category = Category.Create(userId, "Food", TransactionType.Expense);
        var transaction = Transaction.Create(
            userId,
            category,
            TransactionType.Expense,
            100m,
            null,
            UtcNow,
            UtcNow);
        category.Deactivate();

        transaction.Update(
            category,
            TransactionType.Expense,
            120m,
            " corrected ",
            UtcNow.AddDays(1));

        Assert.Equal(120m, transaction.Amount);
        Assert.Equal("corrected", transaction.Description);
        Assert.Equal(UtcNow.AddDays(1), transaction.OccurredAtUtc);
    }

    [Fact]
    public void Update_WithAnotherInactiveCategory_Throws()
    {
        var userId = Guid.NewGuid();
        var originalCategory = Category.Create(userId, "Food", TransactionType.Expense);
        var inactiveCategory = Category.Create(userId, "Transport", TransactionType.Expense);
        inactiveCategory.Deactivate();
        var transaction = Transaction.Create(
            userId,
            originalCategory,
            TransactionType.Expense,
            100m,
            null,
            UtcNow,
            UtcNow);

        Assert.Throws<DomainException>(() => transaction.Update(
            inactiveCategory,
            TransactionType.Expense,
            100m,
            null,
            UtcNow));
    }
}
