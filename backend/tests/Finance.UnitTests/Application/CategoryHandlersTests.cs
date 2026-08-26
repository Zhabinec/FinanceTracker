using Finance.Application.Abstractions;
using Finance.Application.Categories;
using Finance.Application.Categories.Create;
using Finance.Application.Categories.Deactivate;
using Finance.Application.Categories.GetCategories;
using Finance.Application.Categories.Rename;
using Finance.Application.Categories.Restore;
using Finance.Domain.Categories;
using Finance.Domain.Transactions;

namespace Finance.UnitTests.Application;

public sealed class CategoryHandlersTests
{
    [Fact]
    public async Task Create_WithAuthenticatedUser_CreatesOwnedCategory()
    {
        var userId = Guid.NewGuid();
        var store = new FakeCategoryStore();
        var handler = new CreateCategoryHandler(new FakeCurrentUser(userId), store);

        var result = await handler.HandleAsync(
            new CreateCategoryCommand("  Salary  ", TransactionType.Income),
            CancellationToken.None);

        Assert.Equal(CategoryMutationStatus.Success, result.Status);
        Assert.Equal(userId, store.Categories.Single().UserId);
        Assert.Equal("Salary", result.Category!.Name);
        Assert.Equal(TransactionType.Income, result.Category.Type);
    }

    [Fact]
    public async Task Create_WithoutAuthenticatedUser_DoesNotWrite()
    {
        var store = new FakeCategoryStore();
        var handler = new CreateCategoryHandler(new FakeCurrentUser(null), store);

        var result = await handler.HandleAsync(
            new CreateCategoryCommand("Salary", TransactionType.Income),
            CancellationToken.None);

        Assert.Equal(CategoryMutationStatus.Unauthorized, result.Status);
        Assert.Empty(store.Categories);
    }

    [Fact]
    public async Task Create_WithInvalidType_ReturnsValidationError()
    {
        var store = new FakeCategoryStore();
        var handler = new CreateCategoryHandler(
            new FakeCurrentUser(Guid.NewGuid()),
            store);

        var result = await handler.HandleAsync(
            new CreateCategoryCommand("Salary", (TransactionType)99),
            CancellationToken.None);

        Assert.Equal(CategoryMutationStatus.InvalidInput, result.Status);
        Assert.Contains("type", result.Errors.Keys);
        Assert.Empty(store.Categories);
    }

    [Fact]
    public async Task Create_WhenUniqueConstraintFails_ReturnsConflict()
    {
        var store = new FakeCategoryStore { AddSucceeds = false };
        var handler = new CreateCategoryHandler(
            new FakeCurrentUser(Guid.NewGuid()),
            store);

        var result = await handler.HandleAsync(
            new CreateCategoryCommand("Food", TransactionType.Expense),
            CancellationToken.None);

        Assert.Equal(CategoryMutationStatus.DuplicateName, result.Status);
    }

    [Fact]
    public async Task GetCategories_PassesOwnershipAndInactiveFilterToStore()
    {
        var userId = Guid.NewGuid();
        var category = Category.Create(userId, "Food", TransactionType.Expense);
        var store = new FakeCategoryStore(category);
        var handler = new GetCategoriesHandler(new FakeCurrentUser(userId), store);

        var result = await handler.HandleAsync(true, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal(userId, store.LastListUserId);
        Assert.True(store.LastIncludeInactive);
    }

    [Fact]
    public async Task Rename_CategoryOwnedByAnotherUser_ReturnsNotFound()
    {
        var ownerId = Guid.NewGuid();
        var category = Category.Create(ownerId, "Food", TransactionType.Expense);
        var handler = new RenameCategoryHandler(
            new FakeCurrentUser(Guid.NewGuid()),
            new FakeCategoryStore(category));

        var result = await handler.HandleAsync(
            new RenameCategoryCommand(category.Id, "Groceries"),
            CancellationToken.None);

        Assert.Equal(CategoryMutationStatus.NotFound, result.Status);
        Assert.Equal("Food", category.Name);
    }

    [Fact]
    public async Task Rename_WhenUniqueConstraintFails_ReturnsConflict()
    {
        var userId = Guid.NewGuid();
        var category = Category.Create(userId, "Food", TransactionType.Expense);
        var store = new FakeCategoryStore(category) { SaveSucceeds = false };
        var handler = new RenameCategoryHandler(new FakeCurrentUser(userId), store);

        var result = await handler.HandleAsync(
            new RenameCategoryCommand(category.Id, "Transport"),
            CancellationToken.None);

        Assert.Equal(CategoryMutationStatus.DuplicateName, result.Status);
    }

    [Fact]
    public async Task Deactivate_OwnedCategory_MarksItInactive()
    {
        var userId = Guid.NewGuid();
        var category = Category.Create(userId, "Food", TransactionType.Expense);
        var store = new FakeCategoryStore(category);
        var handler = new DeactivateCategoryHandler(new FakeCurrentUser(userId), store);

        var result = await handler.HandleAsync(category.Id, CancellationToken.None);

        Assert.Equal(CategoryMutationStatus.Success, result.Status);
        Assert.False(category.IsActive);
        Assert.Equal(1, store.Saves);
    }

    [Fact]
    public async Task Restore_InactiveOwnedCategory_MarksItActive()
    {
        var userId = Guid.NewGuid();
        var category = Category.Create(userId, "Food", TransactionType.Expense);
        category.Deactivate();
        var store = new FakeCategoryStore(category);
        var handler = new RestoreCategoryHandler(new FakeCurrentUser(userId), store);

        var result = await handler.HandleAsync(category.Id, CancellationToken.None);

        Assert.Equal(CategoryMutationStatus.Success, result.Status);
        Assert.True(category.IsActive);
        Assert.Equal(1, store.Saves);
    }

    private sealed class FakeCurrentUser(Guid? userId) : ICurrentUser
    {
        public Guid? UserId { get; } = userId;
    }

    private sealed class FakeCategoryStore(params Category[] categories) : ICategoryStore
    {
        public List<Category> Categories { get; } = [.. categories];
        public bool AddSucceeds { get; init; } = true;
        public bool SaveSucceeds { get; init; } = true;
        public int Saves { get; private set; }
        public Guid? LastListUserId { get; private set; }
        public bool LastIncludeInactive { get; private set; }

        public Task<IReadOnlyList<Category>> ListByUserAsync(
            Guid userId,
            bool includeInactive,
            CancellationToken cancellationToken)
        {
            LastListUserId = userId;
            LastIncludeInactive = includeInactive;
            IReadOnlyList<Category> result = Categories;
            return Task.FromResult(result);
        }

        public Task<Category?> FindOwnedAsync(
            Guid categoryId,
            Guid userId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Categories.SingleOrDefault(
                category => category.Id == categoryId && category.UserId == userId));
        }

        public Task<bool> TryAddAsync(
            Category category,
            CancellationToken cancellationToken)
        {
            if (AddSucceeds)
            {
                Categories.Add(category);
            }

            return Task.FromResult(AddSucceeds);
        }

        public Task<bool> TrySaveAsync(CancellationToken cancellationToken)
        {
            Saves++;
            return Task.FromResult(SaveSucceeds);
        }
    }
}
