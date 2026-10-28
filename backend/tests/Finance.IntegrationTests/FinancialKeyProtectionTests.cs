using System.Security.Cryptography;
using Finance.Domain.Common;
using Finance.Domain.Users;
using Finance.Infrastructure.Security;

namespace Finance.IntegrationTests;

public sealed class FinancialKeyProtectionTests
{
    private static string Key() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    [Fact]
    public void ProtectedCodesAreRandomizedAndBoundToOwnerVaultAndKeyVersion()
    {
        var master = Key();
        var protection = new FinancialKeyProtection(new(true, "v1", new Dictionary<string, string> { ["v1"] = master }));
        var user = Guid.NewGuid(); var vault = Guid.NewGuid();
        var code = FinancialKeyProtection.GenerateCode();
        var first = protection.Protect(user, vault, code, DateTimeOffset.UtcNow);
        var second = protection.Protect(user, vault, code, DateTimeOffset.UtcNow);
        Assert.NotEqual(first.Nonce, second.Nonce);
        Assert.NotEqual(first.Ciphertext, second.Ciphertext);
        Assert.DoesNotContain(code, first.Ciphertext);
        Assert.Equal(code, protection.Unprotect(first));
        Assert.ThrowsAny<CryptographicException>(() => protection.Unprotect(ManagedFinancialKey.Create(Guid.NewGuid(), vault, "v1", first.Nonce, first.Ciphertext, DateTimeOffset.UtcNow)));
        Assert.ThrowsAny<CryptographicException>(() => protection.Unprotect(ManagedFinancialKey.Create(user, Guid.NewGuid(), "v1", first.Nonce, first.Ciphertext, DateTimeOffset.UtcNow)));
        var rotated = new FinancialKeyProtection(new(true, "v2", new Dictionary<string, string> { ["v1"] = master, ["v2"] = Key() }));
        Assert.Equal(code, rotated.Unprotect(first));
        var missing = new FinancialKeyProtection(new(true, "v2", new Dictionary<string, string> { ["v2"] = Key() }));
        Assert.Throws<InvalidOperationException>(() => missing.Unprotect(first));
    }
    [Fact]
    public void EnrollmentMustDecryptTheActualVaultWithItsOwnerAndRevision()
    {
        var code = FinancialKeyProtection.GenerateCode();
        var user = Guid.NewGuid(); var id = Guid.NewGuid();
        var encrypted = FinancialKeyProtection.EncryptDocument("{\"format\":1}", code, user, id);
        var vault = PrivateFinancialVault.Stage(user, id, Guid.NewGuid(), encrypted.Nonce, encrypted.Ciphertext, new string('A', 64), DateTimeOffset.UtcNow);
        Assert.Equal(code, FinancialKeyProtection.NormalizeAndVerify(code, vault));
        Assert.Throws<DomainException>(() => FinancialKeyProtection.NormalizeAndVerify(FinancialKeyProtection.GenerateCode(), vault));
        Assert.Throws<DomainException>(() => FinancialKeyProtection.NormalizeAndVerify(code + "broken", vault));
        var other = PrivateFinancialVault.Stage(Guid.NewGuid(), id, Guid.NewGuid(), encrypted.Nonce, encrypted.Ciphertext, new string('A', 64), DateTimeOffset.UtcNow);
        Assert.Throws<DomainException>(() => FinancialKeyProtection.NormalizeAndVerify(code, other));
        var codes = Enumerable.Range(0, 1000).Select(_ => FinancialKeyProtection.GenerateCode()).ToArray();
        Assert.Equal(codes.Length, codes.Distinct().Count());
    }
    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    public void EnabledModeRejectsMissingOrMalformedWrappingKeys(string value) =>
        Assert.Throws<InvalidOperationException>(() => new FinancialKeyProtection(new(true, "v1", new Dictionary<string, string> { ["v1"] = value })));
}
