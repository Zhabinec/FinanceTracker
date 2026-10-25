using Finance.Application.Abstractions;
using Finance.Application.Transactions;
using Finance.Application.Transactions.Create;
using Finance.Application.Transactions.Delete;
using Finance.Application.Transactions.GetTransactions;
using Finance.Application.Transactions.Update;
using Finance.Domain.Categories;
using Finance.Domain.Transactions;
using FinanceTransaction = Finance.Domain.Transactions.Transaction;

namespace Finance.UnitTests.Application;

public sealed class TransactionHandlersTests
{
    private static readonly DateTimeOffset UtcNow =
        new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Create_WithValidData_StoresUtcTransactionForMoscowDate()
    {
        var userId = Guid.NewGuid();
        var category = Category.Create(userId, "Food", TransactionType.Expense);
        var transactionStore = new FakeTransactionStore();
        var handler = new CreateTransactionHandler(
            new FakeCurrentUser(userId),
            new FakeCategoryStore(category),
            transactionStore,
            new FakeClock(UtcNow));

        var result = await handler.HandleAsync(
            new CreateTransactionCommand(
                category.Id,
                TransactionType.Expense,
                250.50m,
                " Lunch ",
                new DateOnly(2026, 9, 21)),
            CancellationToken.None);

        Assert.Equal(TransactionMutationStatus.Success, result.Status);
        var saved = Assert.Single(transactionStore.Transactions);
        Assert.Equal(new DateTimeOffset(2026, 9, 20, 21, 0, 0, TimeSpan.Zero), saved.OccurredAtUtc);
        Assert.Equal(UtcNow, saved.CreatedAtUtc);
        Assert.Equal("Lunch", saved.Description);
        Assert.Equal(1, transactionStore.Saves);
    }

    [Fact]
    public async Task Create_WithAnotherUsersCategory_ReturnsNotFoundAndDoesNotWrite()
    {
        var category = Category.Create(Guid.NewGuid(), "Food", TransactionType.Expense);
        var transactionStore = new FakeTransactionStore();
        var handler = new CreateTransactionHandler(
            new FakeCurrentUser(Guid.NewGuid()),
            new FakeCategoryStore(category),
            transactionStore,
            new FakeClock(UtcNow));

        var result = await handler.HandleAsync(
            new CreateTransactionCommand(
                category.Id,
                TransactionType.Expense,
                100m,
                null,
                new DateOnly(2026, 9, 21)),
            CancellationToken.None);

        Assert.Equal(TransactionMutationStatus.CategoryNotFound, result.Status);
        Assert.Empty(transactionStore.Transactions);
    }

    [Fact]
    public async Task Create_WithInactiveCategory_ReturnsConflictStatus()
    {
        var userId = Guid.NewGuid();
        var category = Category.Create(userId, "Food", TransactionType.Expense);
        category.Deactivate();
        var handler = new CreateTransactionHandler(
            new FakeCurrentUser(userId),
            new FakeCategoryStore(category),
            new FakeTransactionStore(),
            new FakeClock(UtcNow));

        var result = await handler.HandleAsync(
            new CreateTransactionCommand(
                category.Id,
                TransactionType.Expense,
                100m,
                null,
                new DateOnly(2026, 9, 21)),
            CancellationToken.None);

        Assert.Equal(TransactionMutationStatus.CategoryInactive, result.Status);
    }

    [Fact]
    public async Task Create_WithMismatchedType_ReturnsValidationError()
    {
        var userId = Guid.NewGuid();
        var category = Category.Create(userId, "Salary", TransactionType.Income);
        var handler = new CreateTransactionHandler(
            new FakeCurrentUser(userId),
            new FakeCategoryStore(category),
            new FakeTransactionStore(),
            new FakeClock(UtcNow));

        var result = await handler.HandleAsync(
            new CreateTransactionCommand(
                category.Id,
                TransactionType.Expense,
                100m,
                null,
                new DateOnly(2026, 9, 21)),
            CancellationToken.None);

        Assert.Equal(TransactionMutationStatus.InvalidInput, result.Status);
        Assert.Contains("type", result.Errors.Keys);
    }

    [Fact]
    public async Task GetTransactions_UsesCurrentUserAndReturnsMoscowDate()
    {
        var userId = Guid.NewGuid();
        var category = Category.Create(userId, "Salary", TransactionType.Income);
        var transaction = FinanceTransaction.Create(
            userId,
            category,
            TransactionType.Income,
            1000m,
            null,
            new DateTimeOffset(2026, 9, 20, 21, 0, 0, TimeSpan.Zero),
            UtcNow);
        var store = new FakeTransactionStore(transaction);
        var handler = new GetTransactionsHandler(new FakeCurrentUser(userId), store);

        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(userId, store.LastListUserId);
        Assert.Equal(new DateOnly(2026, 9, 21), Assert.Single(result).OccurredOn);
    }

    [Fact]
    public async Task Update_AnotherUsersTransaction_ReturnsNotFound()
    {
        var ownerId = Guid.NewGuid();
        var category = Category.Create(ownerId, "Food", TransactionType.Expense);
        var transaction = FinanceTransaction.Create(
            ownerId,
            category,
            TransactionType.Expense,
            100m,
            null,
            UtcNow,
            UtcNow);
        var handler = new UpdateTransactionHandler(
            new FakeCurrentUser(Guid.NewGuid()),
            new FakeCategoryStore(category),
            new FakeTransactionStore(transaction));

        var result = await handler.HandleAsync(
            new UpdateTransactionCommand(
                transaction.Id,
                category.Id,
                TransactionType.Expense,
                200m,
                null,
                new DateOnly(2026, 9, 21)),
            CancellationToken.None);

        Assert.Equal(TransactionMutationStatus.NotFound, result.Status);
        Assert.Equal(100m, transaction.Amount);
    }

    [Fact]
    public async Task Update_CanKeepSameInactiveCategory()
    {
        var userId = Guid.NewGuid();
        var category = Category.Create(userId, "Food", TransactionType.Expense);
        var transaction = FinanceTransaction.Create(
            userId,
            category,
            TransactionType.Expense,
            100m,
            null,
            UtcNow,
            UtcNow);
        category.Deactivate();
        var store = new FakeTransactionStore(transaction);
        var handler = new UpdateTransactionHandler(
            new FakeCurrentUser(userId),
            new FakeCategoryStore(category),
            store);

        var result = await handler.HandleAsync(
            new UpdateTransactionCommand(
                transaction.Id,
                category.Id,
                TransactionType.Expense,
                125m,
                null,
                new DateOnly(2026, 9, 21)),
            CancellationToken.None);

        Assert.Equal(TransactionMutationStatus.Success, result.Status);
        Assert.Equal(125m, transaction.Amount);
        Assert.Equal(1, store.Saves);
    }

    [Fact]
    public async Task Delete_AnotherUsersTransaction_ReturnsNotFoundWithoutDeleting()
    {
        var ownerId = Guid.NewGuid();
        var category = Category.Create(ownerId, "Food", TransactionType.Expense);
        var transaction = FinanceTransaction.Create(
            ownerId,
            category,
            TransactionType.Expense,
            100m,
            null,
            UtcNow,
            UtcNow);
        var store = new FakeTransactionStore(transaction);
        var handler = new DeleteTransactionHandler(
            new FakeCurrentUser(Guid.NewGuid()),
            store);

        var result = await handler.HandleAsync(transaction.Id, CancellationToken.None);

        Assert.Equal(DeleteTransactionStatus.NotFound, result);
        Assert.Single(store.Transactions);
        Assert.Equal(0, store.Saves);
    }

    private sealed class FakeCurrentUser(Guid? userId) : ICurrentUser
    {
        public Guid? UserId { get; } = userId;
    }

    private sealed class FakeClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class FakeCategoryStore(params Category[] categories) : ICategoryStore
    {
        private readonly List<Category> categories = [.. categories];

        public Task<IReadOnlyList<Category>> ListByUserAsync(
            Guid userId,
            bool includeInactive,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Category>>(categories
                .Where(category => category.UserId == userId)
                .ToArray());

        public Task<Category?> FindOwnedAsync(
            Guid categoryId,
            Guid userId,
            CancellationToken cancellationToken) =>
            Task.FromResult(categories.SingleOrDefault(category =>
                category.Id == categoryId && category.UserId == userId));

        public Task<bool> TryAddAsync(Category category, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task<bool> TrySaveNameChangeAsync(CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task SaveChangesAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class FakeTransactionStore(params FinanceTransaction[] transactions)
        : ITransactionStore
    {
        public List<FinanceTransaction> Transactions { get; } = [.. transactions];
        public int Saves { get; private set; }
        public Guid? LastListUserId { get; private set; }

        public Task<IReadOnlyList<TransactionRecord>> ListByUserAsync(
            Guid userId,
            CancellationToken cancellationToken)
        {
            LastListUserId = userId;
            return Task.FromResult<IReadOnlyList<TransactionRecord>>(Transactions
                .Where(transaction => transaction.UserId == userId)
                .Select(transaction => new TransactionRecord(transaction, "Category"))
                .ToArray());
        }

        public Task<FinanceTransaction?> FindOwnedAsync(
            Guid transactionId,
            Guid userId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Transactions.SingleOrDefault(transaction =>
                transaction.Id == transactionId && transaction.UserId == userId));

        public void Add(FinanceTransaction transaction) => Transactions.Add(transaction);

        public void Remove(FinanceTransaction transaction) => Transactions.Remove(transaction);

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            Saves++;
            return Task.CompletedTask;
        }
    }
}
