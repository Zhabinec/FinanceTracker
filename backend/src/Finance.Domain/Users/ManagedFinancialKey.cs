using Finance.Domain.Common;

namespace Finance.Domain.Users;

public sealed class ManagedFinancialKey
{
    private ManagedFinancialKey() { }
    public Guid UserId { get; private set; }
    public Guid VaultId { get; private set; }
    public string KeyId { get; private set; } = "";
    public string Nonce { get; private set; } = "";
    public string Ciphertext { get; private set; } = "";
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static ManagedFinancialKey Create(Guid userId, Guid vaultId, string keyId, string nonce, string ciphertext, DateTimeOffset now)
    {
        if (userId == Guid.Empty || vaultId == Guid.Empty || keyId.Length is < 1 or > 32 ||
            nonce.Length != 16 || ciphertext.Length is < 24 or > 256 || now.Offset != TimeSpan.Zero)
            throw new DomainException("Некорректная настройка доступа к финансам.");
        return new() { UserId = userId, VaultId = vaultId, KeyId = keyId, Nonce = nonce, Ciphertext = ciphertext, CreatedAtUtc = now };
    }
}
