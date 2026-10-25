using Finance.Application.Common;
using Finance.Application.Transactions.GetTransactions;

namespace Finance.UnitTests.Application;

public sealed class TransactionQueryTests
{
    [Theory]
    [InlineData(0, 20, "dateDesc")]
    [InlineData(1, 101, "dateDesc")]
    [InlineData(1000001, 20, "dateDesc")]
    [InlineData(1, 20, "sql")]
    public void Validate_RejectsInvalidPagingAndSort(int page, int size, string sort) =>
        Assert.Throws<RequestValidationException>(() => new TransactionQuery(Page: page, PageSize: size, Sort: sort).Validate());

    [Fact]
    public void Validate_RejectsReversedPeriod() => Assert.Throws<RequestValidationException>(() =>
        new TransactionQuery(From: new DateOnly(2026, 2, 1), To: new DateOnly(2026, 1, 1)).Validate());

    [Fact]
    public void Validate_AcceptsInclusiveSingleDay() =>
        new TransactionQuery(From: new DateOnly(2026, 1, 1), To: new DateOnly(2026, 1, 1)).Validate();
}
