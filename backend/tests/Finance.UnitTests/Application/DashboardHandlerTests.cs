using Finance.Application.Abstractions;
using Finance.Application.Common;
using Finance.Application.Dashboard;
using Finance.Domain.Transactions;

namespace Finance.UnitTests.Application;

public sealed class DashboardHandlerTests
{
    [Theory]
    [InlineData(1000, 250, "750.00")]
    [InlineData(100, 250, "-150.00")]
    [InlineData(0, 0, "0.00")]
    public async Task Handle_ComputesNetResultAndUsesOwnedMoscowPeriod(decimal income, decimal expense, string expected)
    {
        var userId = Guid.NewGuid();
        var store = new Store(new DashboardSnapshot([
            new CategoryTotal(Guid.NewGuid(), "Salary", TransactionType.Income, income),
            new CategoryTotal(Guid.NewGuid(), "Food", TransactionType.Expense, expense)], []));
        var handler = new DashboardHandler(new CurrentUser(userId), store, new Clock());

        var result = await handler.HandleAsync(null, null, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(expected, result.Result);
        Assert.Equal(userId, store.UserId);
        Assert.Equal(new DateTimeOffset(2026, 9, 30, 21, 0, 0, TimeSpan.Zero), store.From);
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 21, 0, 0, TimeSpan.Zero), store.To);
    }

    [Fact]
    public async Task Handle_AnonymousUser_DoesNotReadStore()
    {
        var store = new Store(new DashboardSnapshot([], []));
        Assert.Null(await new DashboardHandler(new CurrentUser(null), store, new Clock())
            .HandleAsync(null, null, CancellationToken.None));
        Assert.Null(store.UserId);
    }

    [Fact]
    public async Task Handle_InvalidPeriod_DoesNotReadStore()
    {
        var store = new Store(new DashboardSnapshot([], []));
        await Assert.ThrowsAsync<RequestValidationException>(() =>
            new DashboardHandler(new CurrentUser(Guid.NewGuid()), store, new Clock()).HandleAsync(
                new DateOnly(2026, 10, 26), new DateOnly(2026, 10, 25), CancellationToken.None));
        Assert.Null(store.UserId);
    }

    private sealed class CurrentUser(Guid? id) : ICurrentUser { public Guid? UserId => id; }
    private sealed class Clock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
    }
    private sealed class Store(DashboardSnapshot snapshot) : IDashboardStore
    {
        public Guid? UserId { get; private set; }
        public DateTimeOffset From { get; private set; }
        public DateTimeOffset To { get; private set; }
        public Task<DashboardSnapshot> ReadAsync(Guid userId, DateTimeOffset fromUtc, DateTimeOffset toExclusiveUtc, CancellationToken cancellationToken)
        {
            UserId = userId; From = fromUtc; To = toExclusiveUtc;
            return Task.FromResult(snapshot);
        }
    }
}
