using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Finance.Application.Abstractions;
using Finance.Application.Auth.Telegram;
using Finance.Infrastructure.Security;

namespace Finance.IntegrationTests;

internal static class TelegramTestData
{
    internal const string BotToken = "123456789:test-only-synthetic-bot-token-never-a-real-secret";
    internal static string Sign(long id, DateTimeOffset? at = null, string? user = null, string? extra = null)
    {
        var fields = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["auth_date"] = (at ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["user"] = user ?? JsonSerializer.Serialize(new { id, first_name = "Test + Пользователь" }),
            ["query_id"] = "test-query"
        };
        if (extra is not null) fields["signature"] = extra;
        var secret = HMACSHA256.HashData(Encoding.UTF8.GetBytes("WebAppData"), Encoding.UTF8.GetBytes(BotToken));
        var hash = HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(string.Join('\n', fields.Select(x => x.Key + "=" + x.Value))));
        return string.Join('&', fields.Select(x => Uri.EscapeDataString(x.Key) + "=" + Uri.EscapeDataString(x.Value))) + "&hash=" + Convert.ToHexString(hash).ToLowerInvariant();
    }
}

public sealed class TelegramProofTests
{
    private sealed class Clock(DateTimeOffset now) : IClock { public DateTimeOffset UtcNow => now; }
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
    private static TelegramInitDataVerifier Verifier(string token = TelegramTestData.BotToken) => new(new TelegramSettings(token, "test_bot"), new Clock(Now));

    [Fact]
    public void AcceptsSignedData_WithLargeId_Unicode_Plus_AndSignatureField()
    {
        var proof = Verifier().Verify(TelegramTestData.Sign(4_503_599_627_370_495, Now, extra: "signature-test"));
        Assert.Equal(4_503_599_627_370_495, proof?.UserId);
    }
    [Theory]
    [InlineData(-601)]
    [InlineData(31)]
    public void RejectsExpiredOrFutureLaunch(int seconds) => Assert.Null(Verifier().Verify(TelegramTestData.Sign(42, Now.AddSeconds(seconds))));
    [Fact]
    public void RejectsTampering_Duplicates_WrongBot_AndDisabledBot()
    {
        var data = TelegramTestData.Sign(42, Now);
        Assert.Null(Verifier().Verify(data.Replace("test-query", "other-query")));
        Assert.Null(Verifier().Verify(data + "&auth_date=1"));
        Assert.Null(Verifier("other-bot").Verify(data));
        Assert.Null(Verifier("").Verify(data));
        Assert.Null(Verifier().Verify(data[..^1]));
    }
    [Theory]
    [InlineData("{\"id\":0}")]
    [InlineData("{\"id\":-1}")]
    [InlineData("{\"id\":4503599627370496}")]
    [InlineData("{\"id\":\"42\"}")]
    [InlineData("{\"id\":42,\"is_bot\":true}")]
    [InlineData("null")]
    [InlineData("{")]
    public void RejectsInvalidUser(string user) => Assert.Null(Verifier().Verify(TelegramTestData.Sign(42, Now, user)));
}

public sealed partial class FinanceApiTests
{
    private async Task<(HttpClient Client, string Email)> TelegramAccount()
    {
        var email = $"telegram-{Guid.NewGuid():N}@example.com";
        var client = fixture.Client(false);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/auth/register", new { email, password = "Test-password-123!" })).StatusCode);
        var token = Uri.UnescapeDataString(fixture.Emails.ConfirmationLinks[email].Split("#token=")[1]);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/confirm-email", new { token })).StatusCode);
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Test-password-123!" });
        client.DefaultRequestHeaders.Authorization = new("Bearer", (await Json(login)).GetProperty("accessToken").GetString());
        return (client, email);
    }
    private static long TelegramId() => Random.Shared.NextInt64(1_000_000, 1_000_000_000_000);
    private static Task<HttpResponseMessage> BindTelegram(HttpClient client, string email, string initData) =>
        client.PostAsJsonAsync("/api/auth/telegram/link", new { email, password = "Test-password-123!", initData });

    [Fact]
    public async Task Telegram_FirstLink_AutomaticLogin_AndSharedFinancialData()
    {
        var (browser, email) = await TelegramAccount();
        using var telegram = fixture.Client(false);
        var initData = TelegramTestData.Sign(TelegramId());
        Assert.True((await Json(await telegram.PostAsJsonAsync("/api/auth/telegram/login", new { initData }))).GetProperty("linkRequired").GetBoolean());
        var binding = await BindTelegram(telegram, email, initData);
        Assert.Equal(HttpStatusCode.OK, binding.StatusCode);
        Assert.False(binding.Headers.Contains("Set-Cookie"));
        var auto = await Json(await telegram.PostAsJsonAsync("/api/auth/telegram/login", new { initData }));
        Assert.False(auto.GetProperty("linkRequired").GetBoolean());
        telegram.DefaultRequestHeaders.Authorization = new("Bearer", auto.GetProperty("session").GetProperty("accessToken").GetString());
        var category = await Category(browser);
        await Transaction(browser, category);
        Assert.Equal(1, (await Json(await telegram.GetAsync("/api/transactions"))).GetProperty("totalCount").GetInt32());
        using var stranger = await User();
        Assert.Equal(0, (await Json(await stranger.GetAsync("/api/transactions"))).GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Telegram_RefreshIsOneTime_AndCannotBecomeBrowserSession()
    {
        var (browser, email) = await TelegramAccount();
        using var telegram = fixture.Client(false);
        var binding = await Json(await BindTelegram(telegram, email, TelegramTestData.Sign(TelegramId())));
        var refreshToken = binding.GetProperty("refreshToken").GetString();
        var rotated = await telegram.PostAsJsonAsync("/api/auth/telegram/refresh", new { refreshToken });
        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
        Assert.False(rotated.Headers.Contains("Set-Cookie"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await telegram.PostAsJsonAsync("/api/auth/telegram/refresh", new { refreshToken })).StatusCode);
        var newToken = (await Json(rotated)).GetProperty("refreshToken").GetString();
        using var cookieAttempt = fixture.Client(false);
        cookieAttempt.DefaultRequestHeaders.Add("Cookie", $"finance.refresh_token={newToken}");
        Assert.Equal(HttpStatusCode.Unauthorized, (await cookieAttempt.PostAsJsonAsync("/api/auth/refresh", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await telegram.PostAsJsonAsync("/api/auth/telegram/logout", new { refreshToken = newToken })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await telegram.PostAsJsonAsync("/api/auth/telegram/refresh", new { refreshToken = newToken })).StatusCode);
        browser.Dispose();
    }

    [Fact]
    public async Task Telegram_UnlinkRevokesAccessAndRefresh_BrowserStillWorks_RelinkDoesNotReviveTokens()
    {
        var (browser, email) = await TelegramAccount();
        using var telegram = fixture.Client(false);
        var initData = TelegramTestData.Sign(TelegramId());
        var original = await Json(await BindTelegram(telegram, email, initData));
        telegram.DefaultRequestHeaders.Authorization = new("Bearer", original.GetProperty("accessToken").GetString());
        var link = await Json(await browser.GetAsync("/api/auth/telegram/link"));
        var version = link.GetProperty("version").GetString();
        Assert.Equal(HttpStatusCode.NoContent, (await browser.DeleteAsync($"/api/auth/telegram/link?version={version}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await telegram.GetAsync("/api/users/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/api/users/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await telegram.PostAsJsonAsync("/api/auth/telegram/refresh", new { refreshToken = original.GetProperty("refreshToken").GetString() })).StatusCode);
        telegram.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.OK, (await BindTelegram(telegram, email, initData)).StatusCode);
        telegram.DefaultRequestHeaders.Authorization = new("Bearer", original.GetProperty("accessToken").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await telegram.GetAsync("/api/users/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await browser.DeleteAsync($"/api/auth/telegram/link?version={version}")).StatusCode);
    }

    [Fact]
    public async Task Telegram_CannotTakeOverEitherSideOfExistingBinding()
    {
        var (first, email) = await TelegramAccount();
        var (other, otherEmail) = await TelegramAccount();
        using var telegram = fixture.Client(false);
        var initData = TelegramTestData.Sign(TelegramId());
        Assert.Equal(HttpStatusCode.OK, (await BindTelegram(telegram, email, initData)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await BindTelegram(telegram, otherEmail, initData)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await BindTelegram(telegram, email, TelegramTestData.Sign(TelegramId()))).StatusCode);
        first.Dispose(); other.Dispose();
    }

    [Fact]
    public async Task Telegram_RejectsForgedExpiredLaunch_AndWrongPassword()
    {
        var (browser, email) = await TelegramAccount();
        using var telegram = fixture.Client(false);
        foreach (var initData in new[] { "user=%7B%22id%22:42%7D", TelegramTestData.Sign(42, DateTimeOffset.UtcNow.AddHours(-1)) })
            Assert.Equal(HttpStatusCode.Unauthorized, (await telegram.PostAsJsonAsync("/api/auth/telegram/login", new { initData })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await telegram.PostAsJsonAsync("/api/auth/telegram/link", new { initData = TelegramTestData.Sign(TelegramId()), email, password = "wrong" })).StatusCode);
        Assert.False((await Json(await browser.GetAsync("/api/auth/telegram/link"))).GetProperty("linked").GetBoolean());
        browser.Dispose();
    }

    [Fact]
    public async Task Telegram_RefreshRacingUnlink_DoesNotPreserveAccess()
    {
        var (browser, email) = await TelegramAccount();
        using var telegram = fixture.Client(false);
        var binding = await Json(await BindTelegram(telegram, email, TelegramTestData.Sign(TelegramId())));
        var link = await Json(await browser.GetAsync("/api/auth/telegram/link"));
        var version = link.GetProperty("version").GetString();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var rotate = telegram.PostAsJsonAsync("/api/auth/telegram/refresh", new { refreshToken = binding.GetProperty("refreshToken").GetString() }, timeout.Token);
        var unlink = browser.DeleteAsync($"/api/auth/telegram/link?version={version}", timeout.Token);
        var responses = await Task.WhenAll(rotate, unlink);
        Assert.Equal(HttpStatusCode.NoContent, responses[1].StatusCode);
        Assert.True(responses[0].StatusCode is HttpStatusCode.OK or HttpStatusCode.Unauthorized);
        if (responses[0].IsSuccessStatusCode)
        {
            var session = await Json(responses[0]);
            telegram.DefaultRequestHeaders.Authorization = new("Bearer", session.GetProperty("accessToken").GetString());
            Assert.Equal(HttpStatusCode.Unauthorized, (await telegram.GetAsync("/api/users/me")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await telegram.PostAsJsonAsync("/api/auth/telegram/refresh", new { refreshToken = session.GetProperty("refreshToken").GetString() })).StatusCode);
        }
        browser.Dispose();
    }

    [Fact]
    public async Task Telegram_PasswordResetRevokesExistingSession()
    {
        var (browser, email) = await TelegramAccount();
        using var telegram = fixture.Client(false);
        var session = await Json(await BindTelegram(telegram, email, TelegramTestData.Sign(TelegramId())));
        await browser.PostAsJsonAsync("/api/auth/forgot-password", new { email });
        var token = Uri.UnescapeDataString(fixture.Emails.Links[email].Split("#token=")[1]);
        Assert.Equal(HttpStatusCode.NoContent, (await browser.PostAsJsonAsync("/api/auth/reset-password", new { token, password = "Changed-password-456!" })).StatusCode);
        telegram.DefaultRequestHeaders.Authorization = new("Bearer", session.GetProperty("accessToken").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await telegram.GetAsync("/api/users/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await telegram.PostAsJsonAsync("/api/auth/telegram/refresh", new { refreshToken = session.GetProperty("refreshToken").GetString() })).StatusCode);
        browser.Dispose();
    }
}
