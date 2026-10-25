namespace Finance.Application.Transactions;

public enum TransactionMutationStatus
{
    Success,
    InvalidInput,
    NotFound,
    CategoryNotFound,
    CategoryInactive,
    Unauthorized
}

public sealed record TransactionMutationResult(
    TransactionMutationStatus Status,
    TransactionDto? Transaction,
    IReadOnlyDictionary<string, string[]> Errors)
{
    public static TransactionMutationResult Success(TransactionDto transaction) =>
        new(TransactionMutationStatus.Success, transaction, EmptyErrors);

    public static TransactionMutationResult Invalid(
        IReadOnlyDictionary<string, string[]> errors) =>
        new(TransactionMutationStatus.InvalidInput, null, errors);

    public static TransactionMutationResult NotFound() =>
        new(TransactionMutationStatus.NotFound, null, EmptyErrors);

    public static TransactionMutationResult CategoryNotFound() =>
        new(TransactionMutationStatus.CategoryNotFound, null, EmptyErrors);

    public static TransactionMutationResult CategoryInactive() =>
        new(TransactionMutationStatus.CategoryInactive, null, new Dictionary<string, string[]>
        {
            ["categoryId"] = ["An inactive category cannot be selected for this transaction."]
        });

    public static TransactionMutationResult Unauthorized() =>
        new(TransactionMutationStatus.Unauthorized, null, EmptyErrors);

    private static readonly IReadOnlyDictionary<string, string[]> EmptyErrors =
        new Dictionary<string, string[]>();
}
