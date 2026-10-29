using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Finance.Domain.Ledger;
using Finance.Application.FinancialRecords;
using Finance.Domain.Common;

namespace Finance.Infrastructure.Security;

// Sequential cipher for one opened financial book. Raw constructor key bytes are wiped;
// disposing the book releases the native cipher context. Do not share between concurrent operations.
public sealed class FinancialRecordProtection : IDisposable
{
    private readonly AesGcm cipher;
    private readonly Guid owner;
    private bool disposed;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };
    public FinancialRecordProtection(Guid owner, string recoveryCode)
    {
        if (owner == Guid.Empty) throw new ArgumentException("Owner is required.", nameof(owner));
        this.owner = owner;
        var parts = recoveryCode.Split('.');
        if (parts.Length != 3 || parts[0] != "FT1" || parts[1].Length != 43 ||
            parts[2] != Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(parts[1])))[..8])
            throw new CryptographicException("Invalid financial key.");
        var key = Convert.FromBase64String(parts[1].Replace('-', '+').Replace('_', '/') + "=");
        try
        {
            if (key.Length != 32) throw new CryptographicException("Invalid financial key size.");
            cipher = new AesGcm(key, 16);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
    private byte[] Aad(FinancialRecord row)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (row.UserId != owner) throw new CryptographicException("Financial record owner mismatch.");
        return Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture,
            $"FinanceTracker:record:v1:{row.UserId:D}:{(int)row.Kind}:{row.Id:D}:{row.Version:D}:{row.OccurredOn:yyyy-MM-dd}:{row.AccountId:D}:{row.CategoryId:D}:{row.CreatedAtUtc.UtcTicks}"));
    }
    public void Encrypt<T>(FinancialRecord row, T value)
    {
        var aad = Aad(row);
        var plain = FinancialRecordPayload.Serialize(value, Json);
        try
        {
            if (plain.Length > 16_368) throw new InvalidOperationException("Financial record payload is too large.");
            var nonce = RandomNumberGenerator.GetBytes(12);
            var encrypted = new byte[plain.Length + 16];
            cipher.Encrypt(nonce, plain, encrypted.AsSpan(0, plain.Length), encrypted.AsSpan(plain.Length), aad);
            row.Protect(nonce, encrypted);
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    public T Decrypt<T>(FinancialRecord row)
    {
        var aad = Aad(row);
        if (row.Nonce.Length != 12 || row.Ciphertext.Length is < 17 or > 16_384)
            throw new CryptographicException("Invalid encrypted financial record.");
        var plain = new byte[row.Ciphertext.Length - 16];
        try
        {
            cipher.Decrypt(row.Nonce, row.Ciphertext.AsSpan(0, plain.Length), row.Ciphertext.AsSpan(plain.Length), plain, aad);
            return FinancialRecordPayload.Deserialize<T>(row, plain, Json);
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    public void Dispose() { if (disposed) return; cipher.Dispose(); disposed = true; }
    public const int MaxBackupBytes = 16_000_000;
    private byte[] BackupAad(Guid vaultId) => Encoding.UTF8.GetBytes($"FinanceTracker:records-backup:v1:{owner}:{vaultId}");
    public EncryptedFinancialBackup EncryptBackup(Guid vaultId, byte[] plain)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (plain.Length > MaxBackupBytes) throw new DomainException("Копия превышает лимит 16 МБ.");
        var nonce = RandomNumberGenerator.GetBytes(12); var encrypted = new byte[plain.Length + 16];
        cipher.Encrypt(nonce, plain, encrypted.AsSpan(0, plain.Length), encrypted.AsSpan(plain.Length), BackupAad(vaultId));
        return new("FinanceTrackerRecordsBackup", 1, vaultId, nonce, encrypted);
    }
    public byte[] DecryptBackup(Guid vaultId, EncryptedFinancialBackup backup)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (backup is null || backup.Format != "FinanceTrackerRecordsBackup" || backup.Version != 1 || backup.VaultId != vaultId ||
            backup.Nonce is not { Length: 12 } || backup.Ciphertext is null || backup.Ciphertext.Length is < 17 or > MaxBackupBytes + 16)
            throw new DomainException("Нужна зашифрованная копия этого аккаунта и хранилища.");
        var plain = new byte[backup.Ciphertext.Length - 16];
        try
        {
            cipher.Decrypt(backup.Nonce, backup.Ciphertext.AsSpan(0, plain.Length), backup.Ciphertext.AsSpan(plain.Length), plain, BackupAad(vaultId));
            return plain;
        }
        catch (CryptographicException) { CryptographicOperations.ZeroMemory(plain); throw new DomainException("Копия повреждена или принадлежит другому аккаунту."); }
    }
    public byte[] DecryptLegacyBackup(Guid vaultId, long revision, string nonce, string ciphertext)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (revision is < 1 or > 9_007_199_254_740_990 || nonce.Length != 16 || ciphertext.Length is < 24 or > 2_666_688)
            throw new DomainException("Некорректная прежняя копия.");
        byte[]? plain = null;
        try
        {
            var encrypted = Convert.FromBase64String(ciphertext); var iv = Convert.FromBase64String(nonce);
            if (iv.Length != 12 || encrypted.Length < 17) throw new FormatException();
            plain = new byte[encrypted.Length - 16];
            cipher.Decrypt(iv, encrypted.AsSpan(0, plain.Length), encrypted.AsSpan(plain.Length), plain,
                Encoding.UTF8.GetBytes($"FinanceTracker:private:v1:{owner}:{vaultId}:{revision}"));
            return plain;
        }
        catch (Exception e) when (e is CryptographicException or FormatException)
        { if (plain is not null) CryptographicOperations.ZeroMemory(plain); throw new DomainException("Прежняя копия повреждена или не подходит к аккаунту."); }
    }
}
