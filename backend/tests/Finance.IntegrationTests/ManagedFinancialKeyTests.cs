using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Finance.Infrastructure.Persistence;
using Finance.Infrastructure.Security;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Finance.IntegrationTests;

public sealed partial class FinanceApiTests
{
    private WebApplicationFactory<Program> ManagedApp() => fixture.App.WithWebHostBuilder(builder =>
    {
        builder.UseSetting("Auth:TelegramOnly", "true");
        builder.UseSetting("FinanceKeys:Enabled", "true");
        builder.UseSetting("FinanceKeys:ActiveKeyId", "test");
        builder.UseSetting("FinanceKeys:Keys:test", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
    });
    private static HttpClient ManagedClient(WebApplicationFactory<Program> app) => app.CreateClient(new WebApplicationFactoryClientOptions
    { BaseAddress = new("https://localhost"), HandleCookies = false, AllowAutoRedirect = false });
    private static async Task<(Guid UserId, long TelegramId)> RegisterManaged(HttpClient client)
    {
        var id = Random.Shared.NextInt64(1, 900_000_000_000_000);
        var response = await client.PostAsJsonAsync("/api/auth/telegram/register", new { initData = TelegramTestData.Sign(id) });
        response.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await Json(response)).GetProperty("accessToken").GetString());
        return ((await Json(await client.GetAsync("/api/users/me"))).GetProperty("id").GetGuid(), id);
    }
    [Fact]
    public async Task ManagedBootstrapIsAtomicIdempotentAndSurvivesANewTelegramSession()
    {
        using var app = ManagedApp(); using var owner = ManagedClient(app); using var other = ManagedClient(app);
        var identity = await RegisterManaged(owner); var otherIdentity = await RegisterManaged(other);
        await Transaction(owner, await Category(owner), "25.00");
        var starts = await Task.WhenAll(owner.PostAsync("/api/private-finance/key/start", null), owner.PostAsync("/api/private-finance/key/start", null));
        foreach (var result in starts) result.EnsureSuccessStatusCode();
        var head = await Json(starts[0]);
        Assert.Equal(head.GetProperty("id").GetGuid(), (await Json(starts[1])).GetProperty("id").GetGuid());
        var repeated = await Json(await owner.PostAsync("/api/private-finance/key/start", null));
        Assert.Equal(head.GetProperty("id").GetGuid(), repeated.GetProperty("id").GetGuid());
        Assert.Equal(1, repeated.GetProperty("revision").GetInt64());
        var keyResponse = await owner.GetAsync("/api/private-finance/key");
        Assert.True(keyResponse.Headers.CacheControl?.NoStore);
        var code = (await Json(keyResponse)).GetProperty("code").GetString();
        Assert.NotNull(code);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
            var stored = await db.ManagedFinancialKeys.SingleAsync(x => x.UserId == identity.UserId);
            Assert.DoesNotContain(code!, stored.Ciphertext);
            var vault = await db.PrivateFinancialVaults.SingleAsync(x => x.UserId == identity.UserId);
            Assert.Equal(code, FinancialKeyProtection.NormalizeAndVerify(code!, vault));
            Assert.Equal(code, scope.ServiceProvider.GetRequiredService<FinancialKeyProtection>().Unprotect(stored));
            using var aes = new AesGcm(Convert.FromBase64String(code!.Split('.')[1].Replace('-', '+').Replace('_', '/') + "="), 16);
            var encrypted = Convert.FromBase64String(vault.Ciphertext); var plain = new byte[encrypted.Length - 16];
            aes.Decrypt(Convert.FromBase64String(vault.Nonce), encrypted.AsSpan(0, plain.Length), encrypted.AsSpan(plain.Length), plain,
                System.Text.Encoding.UTF8.GetBytes($"FinanceTracker:private:v1:{identity.UserId}:{vault.Id}:1"));
            using var document = System.Text.Json.JsonDocument.Parse(plain);
            Assert.Equal("25.00", document.RootElement.GetProperty("transactions")[0].GetProperty("amount").GetString());
            Assert.False(await db.Transactions.AnyAsync(x => x.UserId == identity.UserId));
            Assert.Equal(7, await db.Categories.CountAsync(x => x.UserId == otherIdentity.UserId));
        }
        Assert.Equal(HttpStatusCode.Conflict, (await owner.GetAsync("/api/transactions")).StatusCode);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, (await Json(await other.GetAsync("/api/private-finance/key"))).GetProperty("code").ValueKind);
        using var device = ManagedClient(app);
        var login = await Json(await device.PostAsJsonAsync("/api/auth/telegram/login", new { initData = TelegramTestData.Sign(identity.TelegramId) }));
        device.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.GetProperty("session").GetProperty("accessToken").GetString());
        Assert.Equal(code, (await Json(await device.GetAsync("/api/private-finance/key"))).GetProperty("code").GetString());
    }
    [Fact]
    public async Task ExistingVaultRequiresCorrectKeyConsentOwnerAndRevisionWithoutChangingCiphertext()
    {
        using var app = ManagedApp(); using var owner = ManagedClient(app); using var other = ManagedClient(app);
        var user = await RegisterManaged(owner); await RegisterManaged(other);
        var legacy = await Json(await owner.GetAsync("/api/private-finance/legacy"));
        var id = Guid.NewGuid(); var code = FinancialKeyProtection.GenerateCode();
        var encrypted = FinancialKeyProtection.EncryptDocument(legacy.GetProperty("document").GetString()!, code, user.UserId, id);
        var stage = await owner.PostAsJsonAsync("/api/private-finance/stage", new { id, mutationId = Guid.NewGuid(), encrypted.Nonce, encrypted.Ciphertext, legacyFingerprint = legacy.GetProperty("fingerprint").GetString(), format = 1 });
        stage.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync("/api/private-finance/activate", new { id, revision = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsync("/api/private-finance/key/start", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync("/api/private-finance/key", new { vaultId = id, expectedRevision = 1, code, consentVersion = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync("/api/private-finance/key", new { vaultId = id, expectedRevision = 1, code = FinancialKeyProtection.GenerateCode(), consentVersion = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await other.PostAsJsonAsync("/api/private-finance/key", new { vaultId = id, expectedRevision = 1, code, consentVersion = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync("/api/private-finance/key", new { vaultId = id, expectedRevision = 2, code, consentVersion = 1 })).StatusCode);
        using (var scope = app.Services.CreateScope()) Assert.False(await scope.ServiceProvider.GetRequiredService<FinanceDbContext>().ManagedFinancialKeys.AnyAsync(x => x.UserId == user.UserId));
        for (var i = 0; i < 2; i++) Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsJsonAsync("/api/private-finance/key", new { vaultId = id, expectedRevision = 1, code, consentVersion = 1 })).StatusCode);
        var unchanged = (await Json(await owner.GetAsync("/api/private-finance"))).GetProperty("vault");
        Assert.Equal(encrypted.Ciphertext, unchanged.GetProperty("ciphertext").GetString());
        Assert.Equal(1, unchanged.GetProperty("revision").GetInt64());
    }
    [Fact]
    public async Task TelegramOnlyModeRejectsBrowserAuthAndPreservesExplicitLegacyTelegramLinking()
    {
        using var app = ManagedApp(); using var anonymous = ManagedClient(app); using var browser = await User();
        foreach (var path in new[] { "/api/auth/google/start", "/api/auth/google/options", "/api/auth/google/callback" })
            Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.PostAsJsonAsync("/api/auth/login", new { email = "test@example.invalid", password = "irrelevant" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/private-finance/key")).StatusCode);
        anonymous.DefaultRequestHeaders.Authorization = browser.DefaultRequestHeaders.Authorization;
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/private-finance/key")).StatusCode);
        var options = await Json(await anonymous.GetAsync("/api/auth/options"));
        Assert.True(options.GetProperty("telegramOnly").GetBoolean());
        Assert.True(options.GetProperty("managedFinancialKeys").GetBoolean());
        Assert.False(options.GetProperty("emailRegistrationEnabled").GetBoolean());
        using var telegram = ManagedClient(app); await RegisterManaged(telegram);
        Assert.Equal(HttpStatusCode.NotFound, (await telegram.DeleteAsync($"/api/auth/telegram/link?version={Guid.NewGuid()}")).StatusCode);
    }
}
