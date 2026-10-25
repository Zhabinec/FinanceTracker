using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Finance.IntegrationTests;

public sealed partial class FinanceApiTests
{
    [Fact]
    public async Task RefreshRacingPasswordReset_DoesNotDeadlock_AndCannotKeepOldAccess()
    {
        using var client = await User();
        var me = await client.GetFromJsonAsync<JsonElement>("/api/users/me");
        var email = me.GetProperty("email").GetString()!;
        var refresh = await client.PostAsJsonAsync("/api/auth/refresh", new { });
        var cookie = refresh.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
        using var resetClient = fixture.Client(false);
        using var refreshClient = fixture.Client(false);
        resetClient.Timeout = TimeSpan.FromSeconds(10);
        refreshClient.Timeout = TimeSpan.FromSeconds(10);
        refreshClient.DefaultRequestHeaders.Add("Cookie", cookie);
        await resetClient.PostAsJsonAsync("/api/auth/forgot-password", new { email });
        var token = Uri.UnescapeDataString(fixture.Emails.Links[email].Split("#token=")[1]);
        var rotateTask = refreshClient.PostAsJsonAsync("/api/auth/refresh", new { });
        var resetTask = resetClient.PostAsJsonAsync("/api/auth/reset-password", new { token, password = "New-password-456!" });
        var responses = await Task.WhenAll(rotateTask, resetTask);
        Assert.Equal(HttpStatusCode.NoContent, responses[1].StatusCode);
        Assert.True(responses[0].StatusCode is HttpStatusCode.OK or HttpStatusCode.Unauthorized);
        if (responses[0].StatusCode == HttpStatusCode.OK)
        {
            var result = await Json(responses[0]);
            refreshClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", result.GetProperty("accessToken").GetString());
            Assert.Equal(HttpStatusCode.Unauthorized, (await refreshClient.GetAsync("/api/users/me")).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/users/me")).StatusCode);
    }
}
