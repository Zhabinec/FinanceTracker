using Finance.Domain.Ledger;
using Finance.Domain.Common;
using Finance.Domain.Transactions;

namespace Finance.UnitTests.Domain;

public sealed class FinancialRulesTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);
    private static readonly AccountData Account = FinancialRules.CreateAccount(Owner, "Основной", "RUB", 0);
    private static readonly CategoryData Category = new(Guid.NewGuid(), "Продукты", TransactionType.Expense, true, Guid.NewGuid());
    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(0.001)]
    public void InvalidAmountsAreRejected(decimal amount) => Assert.Throws<DomainException>(() =>
        FinancialRules.Create(Owner, Account, Category, TransactionType.Expense, amount, "Чипсы", new(2026, 10, 29), Now));
    [Fact]
    public void ArchivedAccountsAndInactiveCategoriesRejectNewExpenses()
    {
        Assert.Throws<DomainException>(() => FinancialRules.Create(Owner, Account with { IsArchived = true }, Category,
            TransactionType.Expense, 25, null, new(2026, 10, 29), Now));
        Assert.Throws<DomainException>(() => FinancialRules.Create(Owner, Account, Category with { IsActive = false },
            TransactionType.Expense, 25, null, new(2026, 10, 29), Now));
        Assert.Throws<DomainException>(() => FinancialRules.Create(Owner, Account, Category,
            TransactionType.Income, 25, null, new(2026, 10, 29), Now));
    }
    [Fact]
    public void EditingHistoricalEntriesPreservesIdentityAndCreatedTimeButChangesVersion()
    {
        var first = FinancialRules.Create(Owner, Account, Category, TransactionType.Expense, 25, " Чипсы ", new(2026, 10, 29), Now);
        var second = FinancialRules.Create(Owner, Account with { IsArchived = true }, Category with { IsActive = false },
            TransactionType.Expense, 30, null, new(2026, 10, 28), Now.AddHours(1), first);
        Assert.Equal(first.Id, second.Id); Assert.Equal(first.CreatedAtUtc, second.CreatedAtUtc);
        Assert.NotEqual(first.Version, second.Version); Assert.Equal("Чипсы", first.Description);
        Assert.Equal(-30, FinancialRules.Signed(second));
    }
}
