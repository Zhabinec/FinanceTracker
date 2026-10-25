using Finance.Domain.Accounts;
using Finance.Domain.Categories;
using Finance.Domain.Common;
using Finance.Domain.Transactions;

namespace Finance.UnitTests.Domain;

public sealed class AccountTests
{
    [Theory]
    [InlineData(-125.50)]
    [InlineData(0)]
    [InlineData(999.99)]
    public void Create_PreservesOpeningBalance_AndNormalizesName(decimal balance)
    {
        var account = Account.Create(Guid.NewGuid(), "  Карта  ", "RUB", balance);
        Assert.Equal(balance, account.OpeningBalance);
        Assert.Equal("Карта", account.Name);
        Assert.Equal("КАРТА", account.NormalizedName);
    }

    [Theory]
    [InlineData(0.001)]
    [InlineData(10000000000000000)]
    public void Create_RejectsInvalidOpeningBalance(decimal balance) =>
        Assert.Throws<DomainException>(() => Account.Create(Guid.NewGuid(), "Карта", "RUB", balance));

    [Fact]
    public void ArchivingAndRenaming_DoNotRewriteCurrencyOrOpeningBalance()
    {
        var account = Account.Create(Guid.NewGuid(), "Карта", "RUB", -100);
        var previousVersion = account.Version;
        account.Change("Старая карта", true);
        Assert.True(account.IsArchived);
        Assert.NotEqual(previousVersion, account.Version);
        Assert.Equal("RUB", account.Currency);
        Assert.Equal(-100, account.OpeningBalance);
    }

    [Fact]
    public void Transaction_CannotUseForeignOrNewArchivedAccount()
    {
        var userId = Guid.NewGuid();
        var category = Category.Create(userId, "Еда", TransactionType.Expense);
        var transaction = Transaction.Create(userId, category, category.Type, 10m, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        Assert.Throws<DomainException>(() => transaction.AssignAccount(Account.Create(Guid.NewGuid(), "Карта", "RUB", 0)));
        var archived = Account.Create(userId, "Архив", "RUB", 0);
        archived.Change(archived.Name, true);
        Assert.Throws<DomainException>(() => transaction.AssignAccount(archived));
    }
}
