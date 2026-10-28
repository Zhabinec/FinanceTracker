using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Finance.Application.Abstractions;
using Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Finance.IntegrationTests;
public sealed partial class FinanceApiTests
{
    [Fact]
    public async Task TelegramRegistrationIsIdempotentWithoutEmail_AndCannotRemoveOnlyLogin()
    {
        using var client = fixture.Client(false);
        var telegramId = TelegramId();
        var initData = TelegramTestData.Sign(telegramId);
        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => client.PostAsJsonAsync("/api/auth/telegram/register", new { initData })));
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var session = await Json(responses[0]);
        client.DefaultRequestHeaders.Authorization = new("Bearer", session.GetProperty("accessToken").GetString());
        var profile = await Json(await client.GetAsync("/api/users/me"));
        var userId = profile.GetProperty("id").GetGuid();
        Assert.Equal(System.Text.Json.JsonValueKind.Null, profile.GetProperty("email").ValueKind);
        Assert.False(responses[0].Headers.Contains("Set-Cookie"));
        using var scope = fixture.App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        Assert.Equal(1, await db.TelegramAccountLinks.CountAsync(x => x.TelegramUserId == telegramId));
        Assert.Equal(1, await db.Accounts.CountAsync(x => x.UserId == userId));
        Assert.Equal(7, await db.Categories.CountAsync(x => x.UserId == userId));
        var user = await db.Users.SingleAsync(x => x.Id == userId);
        Assert.Null(user.NormalizedEmail);
        var link = await db.TelegramAccountLinks.SingleAsync(x => x.UserId == userId);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/auth/telegram/link?version={link.Version}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/users/me")).StatusCode);
        var oldRefresh = session.GetProperty("refreshToken").GetString();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/telegram/refresh", new { refreshToken = oldRefresh })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/telegram/refresh", new { refreshToken = oldRefresh })).StatusCode);
        // A valid generic JWT is insufficient for a Telegram-only account: the binding version is mandatory.
        var generic = scope.ServiceProvider.GetRequiredService<IAccessTokenProvider>().Create(user);
        client.DefaultRequestHeaders.Authorization = new("Bearer", generic.Value);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/users/me")).StatusCode);
    }

    [Fact]
    public async Task TelegramAccountsHaveSeparateRecords_AndForgedLaunchCannotRegister()
    {
        using var first = fixture.Client(false);
        using var second = fixture.Client(false);
        Assert.Equal(HttpStatusCode.Unauthorized, (await first.PostAsJsonAsync("/api/auth/telegram/register", new { initData = "user=untrusted" })).StatusCode);
        var firstSession = await Json(await first.PostAsJsonAsync("/api/auth/telegram/register", new { initData = TelegramTestData.Sign(TelegramId()) }));
        var secondSession = await Json(await second.PostAsJsonAsync("/api/auth/telegram/register", new { initData = TelegramTestData.Sign(TelegramId()) }));
        first.DefaultRequestHeaders.Authorization = new("Bearer", firstSession.GetProperty("accessToken").GetString());
        second.DefaultRequestHeaders.Authorization = new("Bearer", secondSession.GetProperty("accessToken").GetString());
        var category = await Json(await first.PostAsJsonAsync("/api/categories", new { name = "Private Telegram category", type = "Expense" }));
        var theirs = await Json(await second.GetAsync("/api/categories"));
        Assert.DoesNotContain(theirs.EnumerateArray(), x => x.GetProperty("id").GetGuid() == category.GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.NotFound, (await second.PostAsync($"/api/categories/{category.GetProperty("id").GetGuid()}/deactivate", null)).StatusCode);
    }
}
