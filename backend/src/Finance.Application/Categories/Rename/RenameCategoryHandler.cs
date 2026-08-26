using Finance.Application.Abstractions;

namespace Finance.Application.Categories.Rename;

public sealed class RenameCategoryHandler(
    ICurrentUser currentUser,
    ICategoryStore categoryStore)
{
    public async Task<CategoryMutationResult> HandleAsync(
        RenameCategoryCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return CategoryMutationResult.Unauthorized();
        }

        var errors = CategoryValidation.ValidateName(command.Name);
        if (errors.Count > 0)
        {
            return CategoryMutationResult.Invalid(errors);
        }

        var category = await categoryStore.FindOwnedAsync(
            command.CategoryId,
            userId,
            cancellationToken);
        if (category is null)
        {
            return CategoryMutationResult.NotFound();
        }

        category.Rename(command.Name!);
        return await categoryStore.TrySaveAsync(cancellationToken)
            ? CategoryMutationResult.Success(CategoryDto.From(category))
            : CategoryMutationResult.Duplicate();
    }
}
