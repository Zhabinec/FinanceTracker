using Finance.Application.Abstractions;
using Finance.Application.Auth.Refresh;
using Finance.Domain.Users;

namespace Finance.UnitTests.Application;

public sealed class RefreshTokensHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_WithActiveSession_RotatesAndReturnsNewTokens()
    {
        var store = new FakeStore(CreateUser());
        var handler = CreateHandler(store);

        var result = await handler.HandleAsync(
            new RefreshTokensCommand("old-token"), CancellationToken.None);

        Assert.Equal(RefreshTokensStatus.Success, result.Status);
        Assert.Equal("access-token", result.AccessToken);
        Assert.Equal("new-refresh-token", result.RefreshToken);
        Assert.Equal("hash:old-token", store.CurrentHash);
        Assert.Equal("hash:new-refresh-token", store.NewHash);
    }

    [Fact]
    public async Task HandleAsync_WithInvalidSession_ReturnsUnauthorizedResult()
    {
        var result = await CreateHandler(new FakeStore(null)).HandleAsync(
            new RefreshTokensCommand("invalid"), CancellationToken.None);

        Assert.Equal(RefreshTokensStatus.InvalidToken, result.Status);
        Assert.Null(result.AccessToken);
    }

    private static RefreshTokensHandler CreateHandler(FakeStore store) =>
        new(store, new FakeProtector(), new FakeAccessTokenProvider(),
            new FakeClock(), new RefreshTokenSettings(30));

    private static User CreateUser() => User.Create(
        "user@example.com", "password-hash", Now, "RUB");

    private sealed class FakeStore(User? user) : IRefreshSessionStore
    {
        public string? CurrentHash { get; private set; }
        public string? NewHash { get; private set; }
        public Task AddAsync(RefreshSession session, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<User?> RotateAsync(string currentTokenHash, string newTokenHash,
            DateTimeOffset nowUtc, DateTimeOffset newExpiresAtUtc, CancellationToken cancellationToken)
        {
            CurrentHash = currentTokenHash;
            NewHash = newTokenHash;
            return Task.FromResult(user);
        }
        public Task RevokeAsync(string tokenHash, DateTimeOffset nowUtc,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeProtector : IRefreshTokenProtector
    {
        public string Generate() => "new-refresh-token";
        public string Hash(string token) => $"hash:{token}";
    }

    private sealed class FakeAccessTokenProvider : IAccessTokenProvider
    {
        public AccessToken Create(User user) => new("access-token", Now.AddMinutes(15));
    }

    private sealed class FakeClock : IClock { public DateTimeOffset UtcNow => Now; }
}
