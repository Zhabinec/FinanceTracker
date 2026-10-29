using Finance.Application.Abstractions;

namespace Finance.Application.Categories.GetCategories;

public sealed class GetCategoriesHandler(
    ICurrentUser currentUser,
    ICategoryStore categoryStore)
{
    public async Task<IReadOnlyList<CategoryDto>?> HandleAsync(
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return null;
        }

        var categories = await categoryStore.ListByUserAsync(
            userId,
            includeInactive,
            cancellationToken);

        return categories.Select(CategoryDto.From).ToArray();
    }
}
