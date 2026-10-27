using Finance.Domain.Common;

namespace Finance.Domain.Users;

public sealed class PrivateFinancialVault
{
    public const int MaxCiphertextBytes = 2_000_016;
    private PrivateFinancialVault() { }
    public Guid UserId { get; private set; }
    public Guid Id { get; private set; }
    public long Revision { get; private set; }
    public Guid MutationId { get; private set; }
    public string Nonce { get; private set; } = "";
    public string Ciphertext { get; private set; } = "";
    public string LegacyFingerprint { get; private set; } = "";
    public bool IsActive { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static PrivateFinancialVault Stage(Guid userId, Guid id, Guid mutationId, string nonce, string ciphertext, string fingerprint, DateTimeOffset now)
    {
        if (userId == Guid.Empty || id == Guid.Empty || fingerprint.Length != 64 || !fingerprint.All(Uri.IsHexDigit))
            throw new DomainException("Некорректное хранилище.");
        var vault = new PrivateFinancialVault { UserId = userId, Id = id, LegacyFingerprint = fingerprint };
        vault.Write(mutationId, nonce, ciphertext, now);
        return vault;
    }

    public void Activate() { IsActive = true; LegacyFingerprint = ""; }
    public void Write(Guid mutationId, string nonce, string ciphertext, DateTimeOffset now)
    {
        if (mutationId == Guid.Empty || now.Offset != TimeSpan.Zero || Revision >= 9_007_199_254_740_990)
            throw new DomainException("Некорректная версия хранилища.");
        if (nonce.Length != 16 || !Convert.TryFromBase64String(nonce, new byte[12], out var length) || length != 12 || nonce == Nonce)
            throw new DomainException("Нужен новый nonce длиной 12 байт.");
        if (ciphertext.Length is < 24 or > 2_666_688 ||
            !Convert.TryFromBase64String(ciphertext, new byte[ciphertext.Length / 4 * 3], out length) || length < 17)
            throw new DomainException("Зашифрованное хранилище превышает лимит 2 МБ или повреждено.");
        MutationId = mutationId; Nonce = nonce; Ciphertext = ciphertext; UpdatedAtUtc = now; Revision++;
    }
}
