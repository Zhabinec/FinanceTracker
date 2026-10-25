using Finance.Application.Abstractions;
using Finance.Application.Auth.Login;
using Finance.Application.Auth.Refresh;
using Finance.Domain.Users;

namespace Finance.UnitTests.Application;

public sealed class LoginUserHandlerTests
{
    private static readonly DateTimeOffset RegistrationTime =
        new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset TokenExpiration =
        RegistrationTime.AddMinutes(15);

    [Fact]
    public async Task HandleAsync_UnconfirmedEmail_DoesNotIssueTokens()
    {
        var user = User.Create("user@example.com", "hashed:correct-password", RegistrationTime, "RUB");
        var handler = new LoginUserHandler(new FakeUserLoginStore(user), new FakePasswordHasher(),
            new FakeAccessTokenProvider(), new FakeRefreshSessionStore(), new FakeRefreshTokenProtector(),
            new FakeClock(), new RefreshTokenSettings(30));
        var result = await handler.HandleAsync(new LoginUserCommand(user.Email, "correct-password"), CancellationToken.None);
        Assert.Equal(LoginUserStatus.EmailNotConfirmed, result.Status);
        Assert.Null(result.AccessToken);
        Assert.Null(result.RefreshToken);
    }

    [Fact]
    public async Task HandleAsync_WithValidCredentials_ReturnsAccessToken()
    {
        var user = CreateUser();
        var handler = new LoginUserHandler(
            new FakeUserLoginStore(user),
            new FakePasswordHasher(),
            new FakeAccessTokenProvider(), new FakeRefreshSessionStore(),
            new FakeRefreshTokenProtector(), new FakeClock(), new RefreshTokenSettings(30));

        var result = await handler.HandleAsync(
            new LoginUserCommand("USER@EXAMPLE.COM", "correct-password"),
            CancellationToken.None);

        Assert.Equal(LoginUserStatus.Success, result.Status);
        Assert.Equal("access-token", result.AccessToken);
        Assert.Equal(TokenExpiration, result.ExpiresAtUtc);
    }

    [Fact]
    public async Task HandleAsync_WithWrongPassword_ReturnsSameInvalidCredentialsResult()
    {
        var handler = new LoginUserHandler(
            new FakeUserLoginStore(CreateUser()),
            new FakePasswordHasher(),
            new FakeAccessTokenProvider(), new FakeRefreshSessionStore(),
            new FakeRefreshTokenProtector(), new FakeClock(), new RefreshTokenSettings(30));

        var result = await handler.HandleAsync(
            new LoginUserCommand("user@example.com", "wrong-password"),
            CancellationToken.None);

        Assert.Equal(LoginUserStatus.InvalidCredentials, result.Status);
        Assert.Null(result.AccessToken);
    }

    [Fact]
    public async Task HandleAsync_WithUnknownEmail_PerformsDummyHashAndReturnsInvalidCredentials()
    {
        var passwordHasher = new FakePasswordHasher();
        var handler = new LoginUserHandler(
            new FakeUserLoginStore(null),
            passwordHasher,
            new FakeAccessTokenProvider(), new FakeRefreshSessionStore(),
            new FakeRefreshTokenProtector(), new FakeClock(), new RefreshTokenSettings(30));

        var result = await handler.HandleAsync(
            new LoginUserCommand("unknown@example.com", "some-password"),
            CancellationToken.None);

        Assert.Equal(LoginUserStatus.InvalidCredentials, result.Status);
        Assert.Equal(1, passwordHasher.HashCalls);
    }

    private static User CreateUser()
    {
        var user = User.Create(
            "User@Example.com",
            "hashed:correct-password",
            RegistrationTime,
            "RUB");
        user.ConfirmEmail(RegistrationTime);
        return user;
    }

    private sealed class FakeUserLoginStore(User? user) : IUserLoginStore
    {
        public Task<User?> FindByNormalizedEmailAsync(
            string normalizedEmail,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(user);
        }
    }

    private sealed class FakePasswordHasher : IPasswordHasher
    {
        public int HashCalls { get; private set; }

        public string Hash(string password)
        {
            HashCalls++;
            return $"hashed:{password}";
        }

        public bool Verify(string passwordHash, string providedPassword) =>
            passwordHash == $"hashed:{providedPassword}";
    }

    private sealed class FakeAccessTokenProvider : IAccessTokenProvider
    {
        public AccessToken Create(User user) =>
            new("access-token", TokenExpiration);
    }

    private sealed class FakeRefreshSessionStore : IRefreshSessionStore
    {
        public Task AddAsync(RefreshSession session, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<User?> RotateAsync(string currentTokenHash, string newTokenHash,
            DateTimeOffset nowUtc, DateTimeOffset newExpiresAtUtc,
            CancellationToken cancellationToken) => Task.FromResult<User?>(null);
        public Task RevokeAsync(string tokenHash, DateTimeOffset nowUtc,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeRefreshTokenProtector : IRefreshTokenProtector
    {
        public string Generate() => "refresh-token";
        public string Hash(string token) => new('A', 64);
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow => RegistrationTime;
    }
}
