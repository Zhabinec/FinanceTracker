using System.Security.Cryptography;
using System.Text;
using Finance.Domain.Ledger;
using Finance.Infrastructure.Security;

namespace Finance.IntegrationTests;

// No PostgreSQL fixture: these cryptography tests also run without Docker.
public sealed class FinancialRecordProtectionTests
{
    private sealed record Payload(string Name, decimal Amount);
    private static FinancialRecord Row(Guid owner) => FinancialRecord.Create(owner, FinancialRecordKind.Transaction,
        Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, new(2026, 10, 29), Guid.NewGuid(), Guid.NewGuid());
    [Fact]
    public void MigrationDocumentOwnsItsBufferAfterTheTemporaryPlaintextIsWiped()
    {
        var owner = Guid.NewGuid(); var vaultId = Guid.NewGuid(); var code = FinancialKeyProtection.GenerateCode();
        var encrypted = FinancialKeyProtection.EncryptDocument("{\"format\":1,\"description\":\"Чипсы\"}", code, owner, vaultId);
        var vault = Finance.Domain.Users.PrivateFinancialVault.Stage(owner, vaultId, Guid.NewGuid(), encrypted.Nonce,
            encrypted.Ciphertext, new string('A', 64), DateTimeOffset.UtcNow);
        using var document = FinancialKeyProtection.DecryptDocument(code, vault);
        Assert.Equal(1, document.RootElement.GetProperty("format").GetInt32());
        Assert.Equal("Чипсы", document.RootElement.GetProperty("description").GetString());
    }
    [Fact]
    public void TransactionStorageDoesNotDuplicateIndexedIdentifiersInsideThePayload()
    {
        var owner = Guid.NewGuid(); var row = Row(owner);
        using var protection = new FinancialRecordProtection(owner, FinancialKeyProtection.GenerateCode());
        var value = new TransactionData(row.Id, row.AccountId!.Value, row.CategoryId!.Value, Finance.Domain.Transactions.TransactionType.Expense,
            25m, "Чипсы", row.OccurredOn!.Value, row.CreatedAtUtc, row.Version);
        protection.Encrypt(row, value);
        Assert.Equal(value, protection.Decrypt<TransactionData>(row));
        using var payload = System.Text.Json.JsonDocument.Parse(protection.Decrypt<System.Text.Json.JsonElement>(row).GetRawText());
        Assert.False(payload.RootElement.TryGetProperty("accountId", out _));
        Assert.True(row.Ciphertext.Length < 160);
    }
    [Fact]
    public void PayloadIsEncryptedWithIndependentNoncesAndExactMoney()
    {
        var owner = Guid.NewGuid(); using var protection = new FinancialRecordProtection(owner, FinancialKeyProtection.GenerateCode());
        var first = Row(owner); var second = Row(owner);
        var value = new Payload("Секретные продукты", 12345678901234.56m);
        protection.Encrypt(first, value); protection.Encrypt(second, value);
        Assert.Equal(value, protection.Decrypt<Payload>(first));
        Assert.NotEqual(first.Nonce, second.Nonce); Assert.NotEqual(first.Ciphertext, second.Ciphertext);
        Assert.DoesNotContain("Секретные", Encoding.UTF8.GetString(first.Ciphertext));
        Assert.Equal(0, first.CreatedAtUtc.Ticks % 10);
    }
    [Theory]
    [InlineData("owner")] [InlineData("kind")] [InlineData("id")] [InlineData("version")]
    [InlineData("date")] [InlineData("account")] [InlineData("category")] [InlineData("created")]
    public void RebindingCiphertextToDifferentMetadataFailsAuthentication(string changed)
    {
        var owner = Guid.NewGuid(); using var protection = new FinancialRecordProtection(owner, FinancialKeyProtection.GenerateCode());
        var original = Row(owner); protection.Encrypt(original, new Payload("Food", 25));
        var other = FinancialRecord.Create(changed == "owner" ? Guid.NewGuid() : owner,
            changed == "kind" ? FinancialRecordKind.Budget : original.Kind,
            changed == "id" ? Guid.NewGuid() : original.Id, changed == "version" ? Guid.NewGuid() : original.Version,
            changed == "created" ? original.CreatedAtUtc.AddSeconds(1) : original.CreatedAtUtc,
            changed == "date" ? original.OccurredOn!.Value.AddDays(1) : original.OccurredOn,
            changed == "account" ? Guid.NewGuid() : original.AccountId, changed == "category" ? Guid.NewGuid() : original.CategoryId);
        other.Protect(original.Nonce, original.Ciphertext);
        Assert.ThrowsAny<CryptographicException>(() => protection.Decrypt<Payload>(other));
    }
    [Fact]
    public void TamperingWrongKeysAndDisposedScopesCannotDecrypt()
    {
        var owner = Guid.NewGuid(); var protection = new FinancialRecordProtection(owner, FinancialKeyProtection.GenerateCode());
        var row = Row(owner); protection.Encrypt(row, new Payload("Food", 25));
        using var other = new FinancialRecordProtection(owner, FinancialKeyProtection.GenerateCode());
        Assert.ThrowsAny<CryptographicException>(() => other.Decrypt<Payload>(row));
        row.Ciphertext[0] ^= 1;
        Assert.ThrowsAny<CryptographicException>(() => protection.Decrypt<Payload>(row));
        protection.Dispose();
        Assert.Throws<ObjectDisposedException>(() => protection.Decrypt<Payload>(row));
    }
}
