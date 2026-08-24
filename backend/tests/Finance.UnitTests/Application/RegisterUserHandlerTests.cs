using Finance.Application.Abstractions;
using Finance.Application.Auth.Register;
using Finance.Domain.Users;

namespace Finance.UnitTests.Application;

public sealed class RegisterUserHandlerTests
{
    private static readonly DateTimeOffset RegistrationTime =
        new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_WithValidCommand_CreatesUser()
    {
        var store = new FakeUserRegistrationStore();
        var handler = CreateHandler(store);

        var result = await handler.HandleAsync(
            new RegisterUserCommand("User@Example.com", "password123", "rub"),
            CancellationToken.None);

        Assert.Equal(RegisterUserStatus.Success, result.Status);
        Assert.Equal("User@Example.com", result.User!.Email);
        Assert.Equal("RUB", result.User.BaseCurrency);
        Assert.Equal(RegistrationTime, result.User.RegisteredAtUtc);
        Assert.NotNull(store.AddedUser);
        Assert.Equal("USER@EXAMPLE.COM", store.AddedUser.NormalizedEmail);
        Assert.Equal("hashed:password123", store.AddedUser.PasswordHash);
    }

    [Fact]
    public async Task HandleAsync_WithInvalidInput_DoesNotAccessStore()
    {
        var store = new FakeUserRegistrationStore();
        var handler = CreateHandler(store);

        var result = await handler.HandleAsync(
            new RegisterUserCommand("not-an-email", "short", "руб"),
            CancellationToken.None);

        Assert.Equal(RegisterUserStatus.InvalidInput, result.Status);
        Assert.Contains("email", result.Errors.Keys);
        Assert.Contains("password", result.Errors.Keys);
        Assert.Contains("baseCurrency", result.Errors.Keys);
        Assert.Equal(0, store.EmailChecks);
        Assert.Null(store.AddedUser);
    }

    [Fact]
    public async Task HandleAsync_WhenEmailExists_ReturnsConflict()
    {
        var store = new FakeUserRegistrationStore { EmailExists = true };
        var handler = CreateHandler(store);

        var result = await handler.HandleAsync(
            new RegisterUserCommand("user@example.com", "password123", "RUB"),
            CancellationToken.None);

        Assert.Equal(RegisterUserStatus.EmailAlreadyExists, result.Status);
        Assert.Null(store.AddedUser);
    }

    [Fact]
    public async Task HandleAsync_WhenConcurrentInsertWins_ReturnsConflict()
    {
        var store = new FakeUserRegistrationStore { AddSucceeds = false };
        var handler = CreateHandler(store);

        var result = await handler.HandleAsync(
            new RegisterUserCommand("user@example.com", "password123", "RUB"),
            CancellationToken.None);

        Assert.Equal(RegisterUserStatus.EmailAlreadyExists, result.Status);
    }

    private static RegisterUserHandler CreateHandler(FakeUserRegistrationStore store)
    {
        return new RegisterUserHandler(
            store,
            new FakePasswordHasher(),
            new FakeClock());
    }

    private sealed class FakeUserRegistrationStore : IUserRegistrationStore
    {
        public bool EmailExists { get; init; }

        public bool AddSucceeds { get; init; } = true;

        public int EmailChecks { get; private set; }

        public User? AddedUser { get; private set; }

        public Task<bool> EmailExistsAsync(
            string normalizedEmail,
            CancellationToken cancellationToken)
        {
            EmailChecks++;
            return Task.FromResult(EmailExists);
        }

        public Task<bool> TryAddAsync(User user, CancellationToken cancellationToken)
        {
            AddedUser = user;
            return Task.FromResult(AddSucceeds);
        }
    }

    private sealed class FakePasswordHasher : IPasswordHasher
    {
        public string Hash(string password) => $"hashed:{password}";

        public bool Verify(string passwordHash, string providedPassword) =>
            passwordHash == $"hashed:{providedPassword}";
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow => RegistrationTime;
    }
}
