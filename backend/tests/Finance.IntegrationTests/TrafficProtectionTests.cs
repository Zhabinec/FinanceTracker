using System.Net;
using System.Net.Http.Json;
using Finance.Application.Abstractions;
using Finance.Domain.Users;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Finance.IntegrationTests;

public sealed partial class FinanceApiTests
{
    [Fact]
    public async Task GlobalBudget_RejectsBeforeJwtDatabaseLookup_AndLeavesStaticRoutesAvailable()
    {
        using var signedIn = await User();
        var profiles = new CountingProfileStore();
        await using var app = fixture.App.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Http:ApiPermitLimit", "1");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IUserProfileStore>();
                services.AddSingleton<IUserProfileStore>(profiles);
            });
        });
        using var client = app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/users/me")).StatusCode);
        client.DefaultRequestHeaders.Authorization = signedIn.DefaultRequestHeaders.Authorization;
        // Forwarded headers must not let the caller manufacture a fresh budget.
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.42");
        var rejected = await client.GetAsync("/api/users/me");
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.NotNull(rejected.Headers.RetryAfter);
        Assert.Equal(0, profiles.Calls);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/missing-static-page")).StatusCode);
    }

    [Fact]
    public async Task Login_AndRegistration_HaveIndependentBoundedBudgets()
    {
        await using var app = fixture.App.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Auth:LoginPermitLimit", "2");
            builder.UseSetting("Auth:RegistrationPermitLimit", "2");
        });
        using var client = app.CreateClient();
        for (var i = 0; i < 2; i++)
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/register", new { email = "", password = "" })).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new { email = "invalid@example.test", password = "wrong" })).StatusCode);
        }
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync("/api/auth/register", new { email = "", password = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync("/api/auth/login", new { email = "invalid@example.test", password = "wrong" })).StatusCode);
        Assert.Equal(404, (int)(await client.GetAsync("/api/no-such-route")).StatusCode);
    }

    private sealed class CountingProfileStore : IUserProfileStore
    {
        public int Calls;
        public Task<User?> FindByIdAsync(Guid userId, CancellationToken cancellationToken)
        { Interlocked.Increment(ref Calls); return Task.FromResult<User?>(null); }
    }
}
