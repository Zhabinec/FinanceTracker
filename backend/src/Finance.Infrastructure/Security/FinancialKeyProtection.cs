using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Finance.Domain.Common;
using Finance.Domain.Users;

namespace Finance.Infrastructure.Security;

public sealed record FinancialKeySettings(bool Enabled, string ActiveKeyId, IReadOnlyDictionary<string, string> Keys);
public sealed class FinancialKeyProtection
{
    private readonly FinancialKeySettings settings;
    public FinancialKeyProtection(FinancialKeySettings settings)
    {
        this.settings = settings;
        if (!settings.Enabled) return;
        if (!settings.Keys.ContainsKey(settings.ActiveKeyId)) throw new InvalidOperationException("Active financial wrapping key is missing.");
        foreach (var pair in settings.Keys)
        {
            if (pair.Key.Length is < 1 or > 32 || !pair.Key.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
                throw new InvalidOperationException("Financial wrapping key id is invalid.");
            byte[] bytes;
            try { bytes = Convert.FromBase64String(pair.Value); }
            catch (FormatException) { throw new InvalidOperationException("Financial wrapping keys must be base64-encoded 32-byte keys."); }
            try { if (bytes.Length != 32) throw new InvalidOperationException("Financial wrapping keys must contain 32 bytes."); }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
    }
    private static byte[] Aad(Guid user, Guid vault, string keyId) => Encoding.UTF8.GetBytes($"FinanceTracker:managed-key:v1:{user}:{vault}:{keyId}");
    public static string GenerateCode()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        try
        {
            var encoded = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            return $"FT1.{encoded}.{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(encoded)))[..8]}";
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    public static (string Nonce, string Ciphertext) EncryptDocument(string document, string code, Guid user, Guid vault)
    {
        var key = Convert.FromBase64String(code.Split('.')[1].Replace('-', '+').Replace('_', '/') + "=");
        var plain = Encoding.UTF8.GetBytes(document);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var encrypted = new byte[plain.Length + 16];
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(nonce, plain, encrypted.AsSpan(0, plain.Length), encrypted.AsSpan(plain.Length),
                Encoding.UTF8.GetBytes($"FinanceTracker:private:v1:{user}:{vault}:1"));
            return (Convert.ToBase64String(nonce), Convert.ToBase64String(encrypted));
        }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(plain); }
    }
    public ManagedFinancialKey Protect(Guid user, Guid vault, string code, DateTimeOffset now)
    {
        if (!settings.Enabled) throw new InvalidOperationException("Managed financial keys are disabled.");
        var key = Convert.FromBase64String(settings.Keys[settings.ActiveKeyId]);
        var bytes = Encoding.UTF8.GetBytes(code);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var encrypted = new byte[bytes.Length + 16];
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(nonce, bytes, encrypted.AsSpan(0, bytes.Length), encrypted.AsSpan(bytes.Length), Aad(user, vault, settings.ActiveKeyId));
            return ManagedFinancialKey.Create(user, vault, settings.ActiveKeyId, Convert.ToBase64String(nonce), Convert.ToBase64String(encrypted), now);
        }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(bytes); }
    }
    public string Unprotect(ManagedFinancialKey value)
    {
        if (!settings.Enabled || !settings.Keys.TryGetValue(value.KeyId, out var configured))
            throw new InvalidOperationException("Required financial wrapping key is unavailable. Restore key configuration; do not replace stored data.");
        var key = Convert.FromBase64String(configured);
        var encrypted = Convert.FromBase64String(value.Ciphertext);
        var plain = new byte[encrypted.Length - 16];
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(Convert.FromBase64String(value.Nonce), encrypted.AsSpan(0, plain.Length), encrypted.AsSpan(plain.Length), plain, Aad(value.UserId, value.VaultId, value.KeyId));
            return Encoding.UTF8.GetString(plain);
        }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(plain); }
    }
    public static string NormalizeAndVerify(string code, PrivateFinancialVault vault)
    {
        if (code is null || code.Length > 100) throw InvalidCode();
        code = string.Concat(code.Where(c => !char.IsWhiteSpace(c)));
        var parts = code.Split('.');
        if (parts.Length != 3 || parts[0] != "FT1" || parts[1].Length != 43 || !parts[1].All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') ||
            parts[2] != Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(parts[1])))[..8]) throw InvalidCode();
        byte[]? key = null, plain = null;
        try
        {
            key = Convert.FromBase64String(parts[1].Replace('-', '+').Replace('_', '/') + "=");
            var encrypted = Convert.FromBase64String(vault.Ciphertext);
            plain = new byte[encrypted.Length - 16];
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(Convert.FromBase64String(vault.Nonce), encrypted.AsSpan(0, plain.Length), encrypted.AsSpan(plain.Length), plain,
                Encoding.UTF8.GetBytes($"FinanceTracker:private:v1:{vault.UserId}:{vault.Id}:{vault.Revision}"));
            using var document = JsonDocument.Parse(plain);
            if (document.RootElement.GetProperty("format").GetInt32() != 1) throw InvalidCode();
            return code;
        }
        catch (Exception e) when (e is CryptographicException or FormatException or JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException)
        { throw InvalidCode(); }
        finally { if (key is not null) CryptographicOperations.ZeroMemory(key); if (plain is not null) CryptographicOperations.ZeroMemory(plain); }
    }
    private static DomainException InvalidCode() => new("Код не подходит к этому хранилищу. Настройки не изменены.");

    public static JsonDocument DecryptDocument(string code, PrivateFinancialVault vault)
    {
        NormalizeAndVerify(code, vault);
        var key = Convert.FromBase64String(code.Split('.')[1].Replace('-', '+').Replace('_', '/') + "=");
        var encrypted = Convert.FromBase64String(vault.Ciphertext);
        var plain = new byte[encrypted.Length - 16];
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(Convert.FromBase64String(vault.Nonce), encrypted.AsSpan(0, plain.Length), encrypted.AsSpan(plain.Length), plain,
                Encoding.UTF8.GetBytes($"FinanceTracker:private:v1:{vault.UserId}:{vault.Id}:{vault.Revision}"));
            return JsonDocument.Parse(plain);
        }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(plain); }
    }
}
