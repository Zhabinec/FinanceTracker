using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Finance.Application.Abstractions;
using Finance.Application.Auth.Telegram;

namespace Finance.Infrastructure.Security;

public sealed class TelegramInitDataVerifier(TelegramSettings settings, IClock clock) : ITelegramInitDataVerifier
{
    public TelegramProof? Verify(string? initData)
    {
        if (!settings.Enabled || string.IsNullOrEmpty(initData) || initData.Length > 8192) return null;
        var fields = new SortedDictionary<string, string>(StringComparer.Ordinal);
        try
        {
            foreach (var part in initData.Split('&'))
            {
                var pair = part.Split('=', 2);
                if (pair.Length != 2) return null;
                var key = Uri.UnescapeDataString(pair[0].Replace('+', ' '));
                var value = Uri.UnescapeDataString(pair[1].Replace('+', ' '));
                if (key.Length is < 1 or > 64 || key.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_') || !fields.TryAdd(key, value)) return null;
            }
            if (fields.Count > 32 || !fields.Remove("hash", out var hash) || hash.Length != 64 || !hash.All(Uri.IsHexDigit)) return null;
            var secret = HMACSHA256.HashData(Encoding.UTF8.GetBytes("WebAppData"), Encoding.UTF8.GetBytes(settings.BotToken));
            var checkString = string.Join('\n', fields.Select(pair => pair.Key + "=" + pair.Value));
            var expected = HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(checkString));
            if (!CryptographicOperations.FixedTimeEquals(expected, Convert.FromHexString(hash))) return null;
            if (!fields.TryGetValue("auth_date", out var authDate) || !long.TryParse(authDate, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)) return null;
            var issued = DateTimeOffset.FromUnixTimeSeconds(seconds);
            var now = clock.UtcNow;
            if (issued > now.AddSeconds(30) || now - issued > TimeSpan.FromSeconds(settings.InitDataLifetimeSeconds)) return null;
            if (!fields.TryGetValue("user", out var userData)) return null;
            using var user = JsonDocument.Parse(userData, new JsonDocumentOptions { MaxDepth = 8 });
            if (user.RootElement.ValueKind != JsonValueKind.Object || !user.RootElement.TryGetProperty("id", out var id) || !id.TryGetInt64(out var userId) ||
                userId is <= 0 or > 4_503_599_627_370_495) return null;
            if (user.RootElement.TryGetProperty("is_bot", out var bot) && bot.ValueKind == JsonValueKind.True) return null;
            return new(userId);
        }
        catch (Exception exception) when (exception is UriFormatException or FormatException or ArgumentOutOfRangeException or JsonException or InvalidOperationException)
        { return null; }
    }
}
