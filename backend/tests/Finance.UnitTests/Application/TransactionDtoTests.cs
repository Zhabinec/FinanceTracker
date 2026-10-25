using Finance.Application.Transactions;
using Finance.Domain.Categories;
using Finance.Domain.Transactions;

namespace Finance.UnitTests.Application;

public sealed class TransactionDtoTests
{
    [Fact]
    public void From_PreservesMaximumAmountAndMoscowCalendarDate()
    {
        var userId = Guid.NewGuid();
        var category = Category.Create(userId, "Salary", TransactionType.Income);
        var utc = new DateTimeOffset(2026, 10, 24, 21, 0, 0, TimeSpan.Zero);
        var transaction = Transaction.Create(userId, category, TransactionType.Income,
            Transaction.MaxAmount, null, utc, utc);

        var dto = TransactionDto.From(transaction, category.Name);

        Assert.Equal("9999999999999999.99", dto.Amount);
        Assert.Equal(new DateOnly(2026, 10, 25), dto.OccurredOn);
    }
}
