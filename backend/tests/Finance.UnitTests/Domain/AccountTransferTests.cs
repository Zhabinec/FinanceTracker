using Finance.Domain.Accounts;
using Finance.Domain.Common;

namespace Finance.UnitTests.Domain;

public sealed class AccountTransferTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(0.001)]
    [InlineData(10000000000000000)]
    public void InvalidAmounts_AreRejected(decimal amount)
    {
        var owner = Guid.NewGuid();
        Assert.Throws<DomainException>(() => AccountTransfer.Create(owner, Guid.NewGuid(), Account.CreatePrimary(owner, "RUB"),
            Account.Create(owner, "Карта", "RUB", 0), amount, null, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Accounts_MustBeDifferentActiveSameCurrencyAndOwned()
    {
        var owner = Guid.NewGuid();
        var source = Account.CreatePrimary(owner, "RUB");
        foreach (var target in new[] { source, Account.Create(Guid.NewGuid(), "Чужой", "RUB", 0), Account.Create(owner, "Евро", "EUR", 0) })
            Assert.Throws<DomainException>(() => AccountTransfer.Create(owner, Guid.NewGuid(), source, target, 25, null, DateTimeOffset.UtcNow));
        var archived = Account.Create(owner, "Архив", "RUB", 0);
        archived.Change(archived.Name, true);
        Assert.Throws<DomainException>(() => AccountTransfer.Create(owner, Guid.NewGuid(), source, archived, 25, null, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Cancel_IsIdempotent_AndDoesNotRewriteTransferHistory()
    {
        var owner = Guid.NewGuid();
        var transfer = AccountTransfer.Create(owner, Guid.NewGuid(), Account.CreatePrimary(owner, "RUB"), Account.Create(owner, "Карта", "RUB", 0),
            25.01m, "  Наличные  ", DateTimeOffset.UtcNow);
        var originalVersion = transfer.Version;
        var cancelledAt = DateTimeOffset.UtcNow;
        transfer.Cancel(cancelledAt);
        Assert.NotEqual(originalVersion, transfer.Version);
        var version = transfer.Version;
        transfer.Cancel(cancelledAt.AddMinutes(1));
        Assert.Equal(version, transfer.Version);
        Assert.Equal(cancelledAt, transfer.CancelledAtUtc);
        Assert.Equal(25.01m, transfer.Amount);
        Assert.Equal("Наличные", transfer.Description);
    }
}
