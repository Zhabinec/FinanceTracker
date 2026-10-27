using Finance.Application.Abstractions;
using Finance.Application.Auth.Google;
using Finance.Application.Auth.Refresh;
using Finance.Domain.Users;

namespace Finance.UnitTests.Application;
public sealed class GoogleAuthHandlerTests
{
    private sealed class Store : IGoogleAuthStore
    {
        public int Calls;
        public CancellationToken Token;
        public Task<User> OpenAsync(GoogleProof proof, GoogleLinkOwner? link, string passwordHash, string tokenHash,
            DateTimeOffset now, DateTimeOffset expires, CancellationToken cancellationToken)
        {
            Calls++; Token = cancellationToken;
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal("hash:generated-secret", passwordHash);
            Assert.Equal("token-hash", tokenHash);
            return Task.FromResult(User.Create(proof.Email, passwordHash, now, "RUB"));
        }
    }
    private sealed class Passwords : IPasswordHasher
    { public string Hash(string value) => "hash:" + value; public bool Verify(string hash, string value) => false; }
    private sealed class Tokens : IRefreshTokenProtector
    { public string Generate() => "generated-secret"; public string Hash(string value) => "token-hash"; }
    private sealed class Clock : IClock { public DateTimeOffset UtcNow => new(2026, 10, 28, 0, 0, 0, TimeSpan.Zero); }
    private static GoogleAuthHandler Handler(Store store) => new(store, new Passwords(), new Tokens(), new Clock(), new RefreshTokenSettings(30));

    [Theory]
    [InlineData("", "user@example.com", true)]
    [InlineData("subject", "bad", true)]
    [InlineData("subject", "user@example.com", false)]
    public async Task UnverifiedOrInvalidIdentityCannotReachStore(string subject, string email, bool verified)
    {
        var store = new Store();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Handler(store).LoginAsync(new(subject, email, verified), null, CancellationToken.None));
        Assert.Equal(0, store.Calls);
    }
    [Fact]
    public async Task CancellationIsForwardedAndNoSessionReturned()
    {
        var store = new Store();
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => Handler(store).LoginAsync(new("subject", "user@example.com", true), null, cancellation.Token));
        Assert.Equal(cancellation.Token, store.Token);
    }
    [Fact]
    public async Task ProviderTokensAreNotUsedAsOurSession()
    {
        var store = new Store();
        var result = await Handler(store).LoginAsync(new("subject", "user@example.com", true), null, CancellationToken.None);
        Assert.Equal("generated-secret", result.RefreshToken);
        Assert.Equal(new Clock().UtcNow.AddDays(30), result.ExpiresAtUtc);
        Assert.Equal(1, store.Calls);
    }
}
