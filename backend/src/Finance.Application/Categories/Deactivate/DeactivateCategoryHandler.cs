using Finance.Application.Abstractions;

namespace Finance.Application.Categories.Deactivate;

public sealed class DeactivateCategoryHandler(
    ICurrentUser currentUser,
    ICategoryStore categoryStore)
{
    public async Task<CategoryMutationResult> HandleAsync(
        Guid categoryId,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return CategoryMutationResult.Unauthorized();
        }

        var category = await categoryStore.FindOwnedAsync(
            categoryId,
            userId,
            cancellationToken);
        if (category is null)
        {
            return CategoryMutationResult.NotFound();
        }

        category.Deactivate();
        await categoryStore.SaveChangesAsync(cancellationToken);
        return CategoryMutationResult.Success(CategoryDto.From(category));
    }
}
