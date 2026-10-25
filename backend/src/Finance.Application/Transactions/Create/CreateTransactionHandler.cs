using Finance.Application.Abstractions;
using Finance.Domain.Transactions;

namespace Finance.Application.Transactions.Create;

public sealed class CreateTransactionHandler(
    ICurrentUser currentUser,
    ICategoryStore categoryStore,
    ITransactionStore transactionStore,
    IClock clock,
    Finance.Application.Accounts.IAccountStore accountStore)
{
    public async Task<TransactionMutationResult> HandleAsync(
        CreateTransactionCommand command,
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

        var category = await categoryStore.FindOwnedAsync(
            command.CategoryId,
            userId,
            cancellationToken);
        if (category is null)
        {
            return TransactionMutationResult.CategoryNotFound();
        }

        if (!category.IsActive)
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

        var account = await accountStore.FindOwnedAsync(command.AccountId ?? userId, userId, cancellationToken);
        if (account is null) return TransactionMutationResult.AccountNotFound();
        if (account.IsArchived) throw new Finance.Application.Common.ConflictException("Архивный счёт не принимает новые операции.");

        var transaction = Transaction.Create(
            userId,
            category,
            command.Type,
            command.Amount,
            command.Description,
            MoscowDate.ToUtc(command.OccurredOn!.Value),
            clock.UtcNow);

        transaction.AssignAccount(account);
        transactionStore.Add(transaction);
        await transactionStore.SaveChangesAsync(cancellationToken);

        return TransactionMutationResult.Success(
            TransactionDto.From(transaction, category.Name));
    }
}
