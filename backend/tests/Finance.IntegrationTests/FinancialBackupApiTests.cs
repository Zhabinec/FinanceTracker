using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Finance.IntegrationTests;

public sealed partial class FinanceApiTests
{
    [Fact]
    public async Task DirectOpenDoesNotProvisionAClientKeyOrDuplicateFinancialState()
    {
        using var app = ManagedApp().WithWebHostBuilder(b => b.UseSetting("FinanceRecords:Enabled", "true"));
        using var client = ManagedClient(app); var identity = await RegisterManaged(client);
        var responses = await Task.WhenAll(client.PostAsync("/api/finance/open", null), client.PostAsync("/api/finance/open", null));
        foreach (var response in responses) response.EnsureSuccessStatusCode();
        foreach (var response in responses) Assert.True((await Json(response)).GetProperty("ready").GetBoolean());
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        Assert.Single(await db.ManagedFinancialKeys.Where(x => x.UserId == identity.UserId).ToListAsync());
        Assert.False(await db.PrivateFinancialVaults.AnyAsync(x => x.UserId == identity.UserId));
        Assert.False(await db.Transactions.AnyAsync(x => x.UserId == identity.UserId));
        Assert.False(await db.Accounts.AnyAsync(x => x.UserId == identity.UserId));
        Assert.Equal(7, (await Json(await client.GetAsync("/api/finance/categories"))).GetArrayLength());
    }
    [Fact]
    public async Task BackupsAreOwnerBoundTamperEvidentAndRestoreWithFreshVersionsAndRevisionGuards()
    {
        using var app = ManagedApp().WithWebHostBuilder(b => b.UseSetting("FinanceRecords:Enabled", "true"));
        using var owner = ManagedClient(app); using var other = ManagedClient(app);
        var identity = await RegisterManaged(owner); await RegisterManaged(other);
        (await owner.PostAsync("/api/finance/open", null)).EnsureSuccessStatusCode(); (await other.PostAsync("/api/finance/open", null)).EnsureSuccessStatusCode();
        var categories = await Json(await owner.GetAsync("/api/finance/categories"));
        var categoryId = categories.EnumerateArray().First(x => x.GetProperty("type").GetString() == "Expense").GetProperty("id").GetGuid();
        var requestId = Guid.NewGuid();
        object Expense(Guid request, decimal amount) => new { requestId = request, accountId = identity.UserId, categoryId, type = "Expense", amount, description = "Private backup marker" };
        var first = await Json(await owner.PostAsJsonAsync("/api/finance/transactions", Expense(requestId, 25m)));
        var backupResponse = await owner.PostAsync("/api/finance/backups", null); backupResponse.EnsureSuccessStatusCode();
        var backup = await Json(backupResponse);
        Assert.DoesNotContain("Private backup marker", backup.GetRawText());
        var preview = await Json(await owner.PostAsJsonAsync("/api/finance/backups/preview", new { backup }));
        Assert.Equal(1, preview.GetProperty("transactions").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, (await other.PostAsJsonAsync("/api/finance/backups/preview", new { backup })).StatusCode);
        var damaged = JsonNode.Parse(backup.GetRawText())!; var bytes = Convert.FromBase64String(damaged["ciphertext"]!.GetValue<string>()); bytes[0] ^= 1; damaged["ciphertext"] = Convert.ToBase64String(bytes);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync("/api/finance/backups/preview", new { backup = damaged })).StatusCode);
        var laterRequest = Guid.NewGuid(); (await owner.PostAsJsonAsync("/api/finance/transactions", Expense(laterRequest, 5m))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync("/api/finance/backups/restore", new { backup, expectedRevision = preview.GetProperty("expectedRevision").GetInt64() })).StatusCode);
        preview = await Json(await owner.PostAsJsonAsync("/api/finance/backups/preview", new { backup }));
        (await owner.PostAsJsonAsync("/api/finance/backups/restore", new { backup, expectedRevision = preview.GetProperty("expectedRevision").GetInt64() })).EnsureSuccessStatusCode();
        var history = await Json(await owner.GetAsync("/api/finance/transactions"));
        Assert.Equal(1, history.GetProperty("totalCount").GetInt32());
        Assert.NotEqual(first.GetProperty("version").GetGuid(), history.GetProperty("items")[0].GetProperty("version").GetGuid());
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync("/api/finance/transactions", Expense(laterRequest, 5m))).StatusCode);
        var accounts = await Json(await owner.GetAsync("/api/finance/accounts")); Assert.Equal("-25.00", accounts[0].GetProperty("balance").GetString());
        var delivery = await Json(await owner.PostAsync("/api/finance/backups/download-ticket", null));
        using var unauthenticated = ManagedClient(app);
        var download = await unauthenticated.GetAsync(delivery.GetProperty("url").GetString()); download.EnsureSuccessStatusCode();
        Assert.True(download.Headers.CacheControl?.NoStore); Assert.Equal("attachment", download.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("https://web.telegram.org", download.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Equal("FinanceTrackerRecordsBackup", (await Json(download)).GetProperty("format").GetString());
        Assert.Equal(HttpStatusCode.Gone, (await unauthenticated.GetAsync(delivery.GetProperty("url").GetString())).StatusCode);
    }
    [Fact]
    public async Task AnOldEncryptedBackupCanBeRestoredAfterMigration()
    {
        using var app = ManagedApp().WithWebHostBuilder(b => b.UseSetting("FinanceRecords:Enabled", "true"));
        using var owner = ManagedClient(app); var identity = await RegisterManaged(owner);
        await Transaction(owner, await Category(owner), "25.00");
        var vault = await Json(await owner.PostAsync("/api/private-finance/key/start", null));
        var backup = new { format = "FinanceTrackerEncryptedBackup", version = 1, userId = identity.UserId, vault, pending = (object?)null };
        (await owner.PostAsync("/api/finance/open", null)).EnsureSuccessStatusCode();
        var preview = await Json(await owner.PostAsJsonAsync("/api/finance/backups/preview", new { backup }));
        Assert.Equal(1, preview.GetProperty("transactions").GetInt32());
        (await owner.PostAsJsonAsync("/api/finance/backups/restore", new { backup, expectedRevision = preview.GetProperty("expectedRevision").GetInt64() })).EnsureSuccessStatusCode();
        Assert.Equal("25.00", (await Json(await owner.GetAsync("/api/finance/transactions"))).GetProperty("items")[0].GetProperty("amount").GetString());
    }
}
