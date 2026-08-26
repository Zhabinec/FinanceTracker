using Finance.Domain.Categories;

namespace Finance.Application.Abstractions;

public interface ICategoryStore
{
    Task<IReadOnlyList<Category>> ListByUserAsync(
        Guid userId,
        bool includeInactive,
        CancellationToken cancellationToken);

    Task<Category?> FindOwnedAsync(
        Guid categoryId,
        Guid userId,
        CancellationToken cancellationToken);

    Task<bool> TryAddAsync(Category category, CancellationToken cancellationToken);

    Task<bool> TrySaveAsync(CancellationToken cancellationToken);
}
