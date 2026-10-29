using System.Security.Cryptography;
using System.Text;
using Finance.Application.Abstractions;
using Finance.Application.Common;

namespace Finance.Api.Security;

// Bounded, short-lived delivery of ciphertext only. No keys, plaintext or persistent bearer URLs.
public sealed class FinancialDownloadTickets(IClock clock)
{
    private sealed record Entry(Guid Owner, byte[] Bytes, DateTimeOffset Expires);
    private readonly Dictionary<string, Entry> entries = [];
    private readonly object gate = new();
    public (string Token, DateTimeOffset Expires) Create(Guid owner, byte[] encryptedFile)
    {
        if (encryptedFile.Length > 23_000_000) throw new InvalidOperationException("Backup delivery limit exceeded.");
        lock (gate)
        {
            foreach (var key in entries.Where(x => x.Value.Owner == owner || x.Value.Expires <= clock.UtcNow).Select(x => x.Key).ToArray()) entries.Remove(key);
            if (entries.Count >= 2) throw new ConflictException("Подготовка копий занята. Повторите через минуту.");
            var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            var expires = clock.UtcNow.AddMinutes(1);
            entries.Add(Hash(token), new(owner, encryptedFile, expires)); return (token, expires);
        }
    }
    public byte[]? Take(string? token, bool consume)
    {
        if (token is null || token.Length != 43 || token.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_'))) return null;
        lock (gate)
        {
            var key = Hash(token);
            if (!entries.TryGetValue(key, out var entry)) return null;
            if (consume || entry.Expires <= clock.UtcNow) entries.Remove(key);
            return entry.Expires > clock.UtcNow ? entry.Bytes : null;
        }
    }
    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
