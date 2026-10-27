using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Finance.Domain.Users;
using Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Finance.IntegrationTests;

public sealed partial class FinanceApiTests
{
    private static (string Nonce, string Ciphertext) EncryptLegacy(string document)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var bytes = Encoding.UTF8.GetBytes(document);
        var ciphertext = new byte[bytes.Length]; var tag = new byte[16];
        using var aes = new AesGcm(RandomNumberGenerator.GetBytes(32), 16);
        aes.Encrypt(nonce, bytes, ciphertext, tag);
        return (Convert.ToBase64String(nonce), Convert.ToBase64String([.. ciphertext, .. tag]));
    }
    private static async Task<JsonElement> StagePrivate(HttpClient user)
    {
        var responseExport = await user.GetAsync("/api/private-finance/legacy");
        Assert.True(responseExport.Headers.CacheControl?.NoStore);
        var legacy = await Json(responseExport);
        var encrypted = EncryptLegacy(legacy.GetProperty("document").GetString()!);
        var response = await user.PostAsJsonAsync("/api/private-finance/stage", new
        {
            id = Guid.NewGuid(), mutationId = Guid.NewGuid(), encrypted.Nonce, encrypted.Ciphertext,
            legacyFingerprint = legacy.GetProperty("fingerprint").GetString(), format = 1
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await Json(response);
    }
    private static Task<HttpResponseMessage> ActivatePrivate(HttpClient user, JsonElement head) =>
        user.PostAsJsonAsync("/api/private-finance/activate", new { id = head.GetProperty("id").GetString(), revision = head.GetProperty("revision").GetInt64() });

    [Fact]
    public async Task PrivateMigration_StagesWithoutDeleting_ActivatesAtomically_AndBlocksLegacyPaths()
    {
        using var owner = await User(); using var other = await User();
        var ownerId = (await Json(await owner.GetAsync("/api/users/me"))).GetProperty("id").GetGuid();
        var category = await Category(owner);
        await Transaction(owner, category, "987654.32");
        var otherCategory = await Category(other); await Transaction(other, otherCategory);
        var staged = await StagePrivate(owner);
        Assert.False(staged.GetProperty("isActive").GetBoolean());
        using (var scope = fixture.App.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
            Assert.Single(await db.Transactions.Where(t => t.UserId == ownerId).ToArrayAsync());
            var stored = await db.PrivateFinancialVaults.SingleAsync(v => v.UserId == ownerId);
            Assert.DoesNotContain("987654.32", stored.Ciphertext);
        }
        foreach (var path in new[] { "/api/accounts", "/api/categories", "/api/transactions", "/api/transfers", "/api/budgets", "/api/dashboard" })
            Assert.Equal(HttpStatusCode.Conflict, (await owner.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync("/api/categories", new { name = "must not leak", type = "Expense" })).StatusCode);
        Assert.Equal(1, (await Json(await other.GetAsync("/api/transactions"))).GetProperty("totalCount").GetInt32());
        Assert.Equal(HttpStatusCode.OK, (await ActivatePrivate(owner, staged)).StatusCode);
        using (var scope = fixture.App.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
            Assert.False(await db.Transactions.AnyAsync(t => t.UserId == ownerId));
            Assert.False(await db.Categories.AnyAsync(t => t.UserId == ownerId));
            Assert.False(await db.Accounts.AnyAsync(t => t.UserId == ownerId));
            Assert.True((await db.PrivateFinancialVaults.SingleAsync(v => v.UserId == ownerId)).IsActive);
            Assert.True(await db.Categories.AnyAsync(t => t.UserId != ownerId));
        }
        Assert.Equal(HttpStatusCode.Conflict, (await owner.GetAsync("/api/private-finance/legacy")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ActivatePrivate(owner, staged)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync("/api/imports/csv/apply", new { })).StatusCode);
    }

    [Fact]
    public async Task PrivateVault_EnforcesOwnership_Revision_Idempotency_AndFreshNonce()
    {
        using var owner = await User(); using var stranger = await User();
        var staged = await StagePrivate(owner); (await ActivatePrivate(owner, staged)).EnsureSuccessStatusCode();
        Assert.Equal(JsonValueKind.Null, (await Json(await stranger.GetAsync("/api/private-finance"))).GetProperty("vault").ValueKind);
        var encrypted = EncryptLegacy("encrypted user document");
        var write = new { id = staged.GetProperty("id").GetString(), expectedRevision = 1, mutationId = Guid.NewGuid(), encrypted.Nonce, encrypted.Ciphertext, format = 1 };
        Assert.Equal(HttpStatusCode.Conflict, (await stranger.PutAsJsonAsync("/api/private-finance", write)).StatusCode);
        var response = await owner.PutAsJsonAsync("/api/private-finance", write);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, (await Json(response)).GetProperty("revision").GetInt64());
        Assert.Equal(2, (await Json(await owner.PutAsJsonAsync("/api/private-finance", write))).GetProperty("revision").GetInt64());
        var stale = new { write.id, expectedRevision = 1, mutationId = Guid.NewGuid(), Nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(12)), write.Ciphertext };
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PutAsJsonAsync("/api/private-finance", stale)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync("/api/private-finance", new { write.id, expectedRevision = 2, mutationId = Guid.NewGuid(), write.Nonce, write.Ciphertext })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync("/api/private-finance", new { write.id, expectedRevision = 2, mutationId = Guid.NewGuid(), nonce = "invalid", ciphertext = "not ciphertext", format = 10 })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.DeleteAsync($"/api/private-finance/stage?id={write.id}&revision=2")).StatusCode);
    }

    [Fact]
    public async Task PrivateMigration_RejectsChangedExport_AndCanCancelPendingWithoutLosingHistory()
    {
        using var user = await User(); var category = await Category(user);
        var old = await Json(await user.GetAsync("/api/private-finance/legacy"));
        await Transaction(user, category);
        var encrypted = EncryptLegacy(old.GetProperty("document").GetString()!);
        Assert.Equal(HttpStatusCode.Conflict, (await user.PostAsJsonAsync("/api/private-finance/stage", new
        { id = Guid.NewGuid(), mutationId = Guid.NewGuid(), encrypted.Nonce, encrypted.Ciphertext, legacyFingerprint = old.GetProperty("fingerprint").GetString() })).StatusCode);
        var pending = await StagePrivate(user);
        Assert.Equal(HttpStatusCode.NoContent, (await user.DeleteAsync($"/api/private-finance/stage?id={pending.GetProperty("id").GetString()}&revision=1")).StatusCode);
        Assert.Equal(1, (await Json(await user.GetAsync("/api/transactions"))).GetProperty("totalCount").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict, (await ActivatePrivate(user, pending)).StatusCode);
    }

    [Fact]
    public async Task PrivateVault_ConcurrentWriters_OnlyOneRevisionWins()
    {
        using var user = await User(); var head = await StagePrivate(user);
        (await ActivatePrivate(user, head)).EnsureSuccessStatusCode();
        var first = EncryptLegacy("first document");
        var second = EncryptLegacy("second document");
        var requests = new[]
        {
            user.PutAsJsonAsync("/api/private-finance", new { id = head.GetProperty("id").GetString(), expectedRevision = 1, mutationId = Guid.NewGuid(), first.Nonce, first.Ciphertext }),
            user.PutAsJsonAsync("/api/private-finance", new { id = head.GetProperty("id").GetString(), expectedRevision = 1, mutationId = Guid.NewGuid(), second.Nonce, second.Ciphertext })
        };
        var responses = await Task.WhenAll(requests);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(2, (await Json(await user.GetAsync("/api/private-finance"))).GetProperty("vault").GetProperty("revision").GetInt64());
    }

    [Fact]
    public async Task PasswordRecovery_RevokesAccessWithoutReplacingThePrivateVault()
    {
        using var user = await User();
        var email = (await Json(await user.GetAsync("/api/users/me"))).GetProperty("email").GetString()!;
        var staged = await StagePrivate(user);
        var active = await Json(await ActivatePrivate(user, staged));
        await user.PostAsJsonAsync("/api/auth/forgot-password", new { email });
        var token = Uri.UnescapeDataString(fixture.Emails.Links[email].Split("#token=")[1]);
        Assert.Equal(HttpStatusCode.NoContent, (await user.PostAsJsonAsync("/api/auth/reset-password", new { token, password = "Changed-private-password-456!" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await user.GetAsync("/api/private-finance")).StatusCode);
        using var recovered = fixture.Client(false);
        var login = await recovered.PostAsJsonAsync("/api/auth/login", new { email, password = "Changed-private-password-456!" });
        login.EnsureSuccessStatusCode();
        recovered.DefaultRequestHeaders.Authorization = new("Bearer", (await Json(login)).GetProperty("accessToken").GetString());
        var stored = (await Json(await recovered.GetAsync("/api/private-finance"))).GetProperty("vault");
        Assert.Equal(active.GetProperty("id").GetString(), stored.GetProperty("id").GetString());
        Assert.Equal(active.GetProperty("ciphertext").GetString(), stored.GetProperty("ciphertext").GetString());
        Assert.Equal(active.GetProperty("nonce").GetString(), stored.GetProperty("nonce").GetString());
    }
}
