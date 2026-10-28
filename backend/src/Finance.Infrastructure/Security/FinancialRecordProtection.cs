using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Finance.Domain.Ledger;

namespace Finance.Infrastructure.Security;

// Scoped to one opened owner key. Disposing the scope wipes the raw key bytes.
public sealed class FinancialRecordProtection : IDisposable
{
    private readonly byte[] key;
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
        key = Convert.FromBase64String(parts[1].Replace('-', '+').Replace('_', '/') + "=");
        if (key.Length != 32) throw new CryptographicException("Invalid financial key size.");
    }
    private byte[] Aad(FinancialRecord row)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (row.UserId != owner) throw new CryptographicException("Financial record owner mismatch.");
        return Encoding.UTF8.GetBytes(string.Join(':', "FinanceTracker:record:v1", row.UserId, (int)row.Kind,
            row.Id, row.Version, row.OccurredOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            row.AccountId?.ToString() ?? "", row.CategoryId?.ToString() ?? "",
            row.CreatedAtUtc.UtcTicks.ToString(CultureInfo.InvariantCulture)));
    }
    public void Encrypt<T>(FinancialRecord row, T value)
    {
        var aad = Aad(row);
        var plain = JsonSerializer.SerializeToUtf8Bytes(value, Json);
        try
        {
            if (plain.Length > 16_368) throw new InvalidOperationException("Financial record payload is too large.");
            var nonce = RandomNumberGenerator.GetBytes(12);
            var encrypted = new byte[plain.Length + 16];
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(nonce, plain, encrypted.AsSpan(0, plain.Length), encrypted.AsSpan(plain.Length), aad);
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
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(row.Nonce, row.Ciphertext.AsSpan(0, plain.Length), row.Ciphertext.AsSpan(plain.Length), plain, aad);
            return JsonSerializer.Deserialize<T>(plain, Json) ?? throw new CryptographicException("Empty financial record.");
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    public void Dispose() { if (disposed) return; CryptographicOperations.ZeroMemory(key); disposed = true; }
}
