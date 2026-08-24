using Finance.Application.Abstractions;
using Finance.Application.Users.GetCurrent;
using Finance.Domain.Users;

namespace Finance.UnitTests.Application;

public sealed class GetCurrentUserHandlerTests
{
    private static readonly DateTimeOffset RegistrationTime =
        new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_WithAuthenticatedExistingUser_ReturnsProfile()
    {
        var user = User.Create(
            "User@Example.com",
            "password-hash",
            RegistrationTime,
            "RUB");
        var handler = new GetCurrentUserHandler(
            new FakeCurrentUser(user.Id),
            new FakeUserProfileStore(user));

        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(user.Id, result.Id);
        Assert.Equal(user.Email, result.Email);
        Assert.Equal(user.BaseCurrency, result.BaseCurrency);
    }

    [Fact]
    public async Task HandleAsync_WithoutAuthenticatedUser_DoesNotQueryStore()
    {
        var store = new FakeUserProfileStore(null);
        var handler = new GetCurrentUserHandler(
            new FakeCurrentUser(null),
            store);

        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(0, store.Queries);
    }

    private sealed class FakeCurrentUser(Guid? userId) : ICurrentUser
    {
        public Guid? UserId { get; } = userId;
    }

    private sealed class FakeUserProfileStore(User? user) : IUserProfileStore
    {
        public int Queries { get; private set; }

        public Task<User?> FindByIdAsync(
            Guid userId,
            CancellationToken cancellationToken)
        {
            Queries++;
            return Task.FromResult(user);
        }
    }
}
