namespace Finance.Application.Categories;

public enum CategoryMutationStatus
{
    Success,
    InvalidInput,
    NotFound,
    DuplicateName,
    Unauthorized
}

public sealed record CategoryMutationResult(
    CategoryMutationStatus Status,
    CategoryDto? Category,
    IReadOnlyDictionary<string, string[]> Errors)
{
    public static CategoryMutationResult Success(CategoryDto category) =>
        new(CategoryMutationStatus.Success, category, EmptyErrors);

    public static CategoryMutationResult Invalid(
        IReadOnlyDictionary<string, string[]> errors) =>
        new(CategoryMutationStatus.InvalidInput, null, errors);

    public static CategoryMutationResult NotFound() =>
        new(CategoryMutationStatus.NotFound, null, EmptyErrors);

    public static CategoryMutationResult Duplicate() =>
        new(
            CategoryMutationStatus.DuplicateName,
            null,
            new Dictionary<string, string[]>
            {
                ["name"] = ["A category with this name and type already exists."]
            });

    public static CategoryMutationResult Unauthorized() =>
        new(CategoryMutationStatus.Unauthorized, null, EmptyErrors);

    private static readonly IReadOnlyDictionary<string, string[]> EmptyErrors =
        new Dictionary<string, string[]>();
}
