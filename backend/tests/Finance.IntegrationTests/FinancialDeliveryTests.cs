using Finance.Api.Security;
using Finance.Application.Abstractions;
using Finance.Application.Common;
using Finance.Domain.Common;
using Finance.Infrastructure.Security;

namespace Finance.IntegrationTests;

public sealed class FinancialDeliveryTests
{
    private sealed class Clock : IClock { public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow; }
    [Fact]
    public void CiphertextTicketsAreBoundedExpireAndAllowOnlyOneDownload()
    {
        var clock = new Clock(); var tickets = new FinancialDownloadTickets(clock);
        var owner = Guid.NewGuid(); var first = tickets.Create(owner, [1, 2, 3]);
        Assert.Equal(new byte[] { 1, 2, 3 }, tickets.Take(first.Token, false));
        Assert.Equal(new byte[] { 1, 2, 3 }, tickets.Take(first.Token, true)); Assert.Null(tickets.Take(first.Token, true));
        var replacement = tickets.Create(owner, [4]); var latest = tickets.Create(owner, [5]);
        Assert.Null(tickets.Take(replacement.Token, false));
        tickets.Create(Guid.NewGuid(), [6]);
        Assert.Throws<ConflictException>(() => tickets.Create(Guid.NewGuid(), [7]));
        clock.UtcNow = clock.UtcNow.AddMinutes(2); Assert.Null(tickets.Take(latest.Token, false));
        Assert.Null(tickets.Take("malformed", true));
    }
    [Fact]
    public void BackupEncryptionBindsTheWholeSnapshotToItsOwnerAndVault()
    {
        var owner = Guid.NewGuid(); var vault = Guid.NewGuid(); var code = FinancialKeyProtection.GenerateCode();
        using var key = new FinancialRecordProtection(owner, code); var file = key.EncryptBackup(vault, "encrypted snapshot"u8.ToArray());
        Assert.Equal("encrypted snapshot"u8.ToArray(), key.DecryptBackup(vault, file));
        using var otherOwner = new FinancialRecordProtection(Guid.NewGuid(), code);
        Assert.Throws<DomainException>(() => otherOwner.DecryptBackup(vault, file));
        Assert.Throws<DomainException>(() => key.DecryptBackup(Guid.NewGuid(), file));
        file.Ciphertext[0] ^= 1; Assert.Throws<DomainException>(() => key.DecryptBackup(vault, file));
    }
    [Fact]
    public void ProductionAssembliesDoNotReferenceHistoricalWriters()
    {
        Assert.DoesNotContain(typeof(Program).Assembly.GetReferencedAssemblies(), x => x.Name == "Finance.LegacyFixtures");
        Assert.Null(typeof(Program).Assembly.GetType("Finance.Api.Controllers.TransactionsController"));
        Assert.Null(typeof(Finance.Application.FinancialRecords.SaveFinancialTransactionHandler).Assembly.GetType("Finance.Application.Transactions.Create.CreateTransactionHandler"));
    }
}
