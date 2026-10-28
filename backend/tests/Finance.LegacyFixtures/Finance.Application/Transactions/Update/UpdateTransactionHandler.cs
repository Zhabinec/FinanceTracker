using Finance.Application.Abstractions;

namespace Finance.Application.Transactions.Update;

public sealed class UpdateTransactionHandler(
    ICurrentUser currentUser,
    ICategoryStore categoryStore,
    ITransactionStore transactionStore,
    Finance.Application.Accounts.IAccountStore accountStore)
{
    public async Task<TransactionMutationResult> HandleAsync(
        UpdateTransactionCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return TransactionMutationResult.Unauthorized();
        }

        var errors = TransactionValidation.Validate(
            command.CategoryId,
            command.Type,
            command.Amount,
            command.Description,
            command.OccurredOn);
        if (errors.Count > 0)
        {
            return TransactionMutationResult.Invalid(errors);
        }

        var transaction = await transactionStore.FindOwnedAsync(
            command.TransactionId,
            userId,
            cancellationToken);
        if (transaction is null)
        {
            return TransactionMutationResult.NotFound();
        }
        if (command.Version is null)
            return TransactionMutationResult.Invalid(new Dictionary<string, string[]> { ["version"] = ["Transaction version is required."] });
        if (transaction.Version != command.Version)
            throw new Finance.Application.Common.ConflictException("Transaction has changed. Reload it before saving.");

        var category = await categoryStore.FindOwnedAsync(
            command.CategoryId,
            userId,
            cancellationToken);
        if (category is null)
        {
            return TransactionMutationResult.CategoryNotFound();
        }

        if (!category.IsActive && category.Id != transaction.CategoryId)
        {
            return TransactionMutationResult.CategoryInactive();
        }

        if (category.Type != command.Type)
        {
            return TransactionMutationResult.Invalid(new Dictionary<string, string[]>
            {
                ["type"] = ["Category type must match transaction type."]
            });
        }

        var account = await accountStore.FindOwnedAsync(command.AccountId ?? transaction.AccountId, userId, cancellationToken);
        if (account is null) return TransactionMutationResult.AccountNotFound();
        if (account.IsArchived && account.Id != transaction.AccountId)
            throw new Finance.Application.Common.ConflictException("Нельзя перенести операцию на архивный счёт.");
        transaction.AssignAccount(account);
        transaction.Update(
            category,
            command.Type,
            command.Amount,
            command.Description,
            MoscowDate.ToUtc(command.OccurredOn!.Value));
        await transactionStore.SaveChangesAsync(cancellationToken);

        return TransactionMutationResult.Success(
            TransactionDto.From(transaction, category.Name));
    }
}
