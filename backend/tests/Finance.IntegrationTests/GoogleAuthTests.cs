using System.Net;
using System.Net.Http.Json;
using Finance.Application.Auth.Google;
using Finance.Application.Common;
using Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Finance.IntegrationTests;
public sealed partial class FinanceApiTests
{
    [Fact]
    public async Task ReadingGoogleOptionsDoesNotConsumePasswordLoginAttempts()
    {
        using var app = fixture.App.WithWebHostBuilder(builder => builder.UseSetting("Auth:LoginPermitLimit", "1"));
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
        for (var i = 0; i < 5; i++) Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/google/options")).StatusCode);
        var credentials = new { email = $"unknown-{Guid.NewGuid():N}@example.com", password = "Test-password-123!" };
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", credentials)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync("/api/auth/login", credentials)).StatusCode);
    }
    private async Task<(Guid User, GoogleSession Session)> GoogleLogin(GoogleProof proof, GoogleLinkOwner? link = null)
    {
        using var scope = fixture.App.Services.CreateScope();
        var session = await scope.ServiceProvider.GetRequiredService<GoogleAuthHandler>().LoginAsync(proof, link, CancellationToken.None);
        var identity = await scope.ServiceProvider.GetRequiredService<FinanceDbContext>().GoogleAccountLinks.AsNoTracking().SingleAsync(x => x.Subject == proof.Subject);
        return (identity.UserId, session);
    }
    private static GoogleProof Proof(string? email = null) => new(Guid.NewGuid().ToString("N"), email ?? $"google-{Guid.NewGuid():N}@example.com", true);

    [Fact]
    public async Task GoogleCreatesConfirmedAccount_AndSubjectKeepsSameAccountAfterEmailChange()
    {
        var proof = Proof();
        var first = await GoogleLogin(proof);
        var next = await GoogleLogin(proof with { Email = $"changed-{Guid.NewGuid():N}@example.com" });
        Assert.Equal(first.User, next.User);
        Assert.NotEqual(first.Session.RefreshToken, next.Session.RefreshToken);
        using var scope = fixture.App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        var user = await db.Users.SingleAsync(x => x.Id == first.User);
        Assert.NotNull(user.EmailConfirmedAtUtc);
        Assert.Equal(proof.Email, user.Email);
        Assert.Equal(1, await db.Accounts.CountAsync(x => x.UserId == first.User));
        Assert.Equal(7, await db.Categories.CountAsync(x => x.UserId == first.User));
        Assert.Equal(2, await db.RefreshSessions.CountAsync(x => x.UserId == first.User));
        Assert.DoesNotContain(first.Session.RefreshToken, (await db.RefreshSessions.FirstAsync(x => x.UserId == first.User)).TokenHash);
    }

    [Fact]
    public async Task GoogleDoesNotMergeByEmail_AndRejectsCrossAccountLinkAndOldStamp()
    {
        var firstProof = Proof();
        var first = await GoogleLogin(firstProof);
        await Assert.ThrowsAsync<ConflictException>(() => GoogleLogin(Proof(firstProof.Email)));
        var secondProof = Proof();
        var second = await GoogleLogin(secondProof);
        using var scope = fixture.App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        var user = await db.Users.SingleAsync(x => x.Id == first.User);
        await Assert.ThrowsAsync<ConflictException>(() => GoogleLogin(secondProof, new(user.Id, user.SecurityStamp)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => GoogleLogin(firstProof, new(user.Id, Guid.NewGuid())));
        Assert.Equal(second.User, (await GoogleLogin(secondProof)).User);
    }

    [Fact]
    public async Task GoogleRejectsUnverifiedEmail_AndDisabledProviderHasNoLoginEndpoint()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => GoogleLogin(Proof() with { EmailVerified = false }));
        using var client = fixture.Client();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/auth/google/start")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/auth/google/complete")).StatusCode);
    }

    [Fact]
    public async Task GoogleChallengeUsesPkceAndNonce_AndUnsolicitedCallbackCannotSignIn()
    {
        using var app = fixture.App.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Google:ClientId", "synthetic-google-test-client");
            builder.UseSetting("Google:ClientSecret", "synthetic-google-test-credential");
            builder.UseSetting("Google:RedirectUri", "https://localhost/api/auth/google/callback");
            builder.ConfigureServices(services => services.Configure<OpenIdConnectOptions>("Google", options =>
                options.Configuration = new OpenIdConnectConfiguration
                {
                    Issuer = "https://accounts.google.com",
                    AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth",
                    TokenEndpoint = "https://oauth2.googleapis.com/token"
                }));
        });
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
        var response = await client.GetAsync("/api/auth/google/start?returnUrl=https://attacker.example/");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var url = response.Headers.Location!;
        Assert.Equal("accounts.google.com", url.Host);
        var query = QueryHelpers.ParseQuery(url.Query);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.False(string.IsNullOrWhiteSpace(query["nonce"]));
        Assert.False(string.IsNullOrWhiteSpace(query["state"]));
        Assert.Equal("https://localhost/api/auth/google/callback", query["redirect_uri"]);
        var callback = await client.GetAsync("/api/auth/google/callback?code=untrusted-code");
        Assert.Equal("/login?google=failed", callback.Headers.Location?.OriginalString);
        Assert.False(callback.Headers.TryGetValues("Set-Cookie", out var cookies) && cookies.Any(x => x.StartsWith("finance.refresh_token=")));
        var complete = await client.GetAsync("/api/auth/google/complete");
        Assert.Equal("/login?google=failed", complete.Headers.Location?.OriginalString);
    }
}
