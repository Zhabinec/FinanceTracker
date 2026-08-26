using Finance.Application.Abstractions;
using Finance.Domain.Categories;

namespace Finance.Application.Categories.Create;

public sealed class CreateCategoryHandler(
    ICurrentUser currentUser,
    ICategoryStore categoryStore)
{
    public async Task<CategoryMutationResult> HandleAsync(
        CreateCategoryCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return CategoryMutationResult.Unauthorized();
        }

        var errors = CategoryValidation.ValidateCreate(command.Name, command.Type);
        if (errors.Count > 0)
        {
            return CategoryMutationResult.Invalid(errors);
        }

        var category = Category.Create(userId, command.Name!, command.Type);
        return await categoryStore.TryAddAsync(category, cancellationToken)
            ? CategoryMutationResult.Success(CategoryDto.From(category))
            : CategoryMutationResult.Duplicate();
    }
}
