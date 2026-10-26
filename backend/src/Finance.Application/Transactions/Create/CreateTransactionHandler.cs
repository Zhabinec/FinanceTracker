using Finance.Application.Abstractions;
using Finance.Domain.Transactions;
using Finance.Application.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

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

        var requestedDate = command.OccurredOn;
        var createdAtUtc = clock.UtcNow;
        command = command with { OccurredOn = command.OccurredOn ?? MoscowDate.FromUtc(createdAtUtc) };
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

        if (command.RequestId == Guid.Empty)
            return TransactionMutationResult.Invalid(new Dictionary<string, string[]> { ["requestId"] = ["Нужен непустой идентификатор запроса."] });
        var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            command.CategoryId, command.Type, Amount = command.Amount.ToString("F2", CultureInfo.InvariantCulture),
            Description = string.IsNullOrWhiteSpace(command.Description) ? null : command.Description.Trim(),
            OccurredOn = requestedDate, AccountId = command.AccountId ?? userId
        })));
        if (command.RequestId is { } requestId && await transactionStore.FindRequestAsync(userId, requestId, cancellationToken) is { } existing)
            return await ReplayAsync(existing, fingerprint, cancellationToken);

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
            createdAtUtc);

        transaction.AssignAccount(account);
        if (command.RequestId is { } newRequestId)
        {
            var request = TransactionRequest.Create(userId, newRequestId, transaction.Id, fingerprint, createdAtUtc);
            var saved = await transactionStore.AddOnceAsync(transaction, request, cancellationToken);
            if (saved.TransactionId != transaction.Id) return await ReplayAsync(saved, fingerprint, cancellationToken);
        }
        else
        {
            // Older API clients remain supported; current clients always supply RequestId.
            transactionStore.Add(transaction);
            await transactionStore.SaveChangesAsync(cancellationToken);
        }

        return TransactionMutationResult.Success(
            TransactionDto.From(transaction, category.Name));
    }

    private async Task<TransactionMutationResult> ReplayAsync(TransactionRequest request, string fingerprint, CancellationToken cancellationToken)
    {
        if (request.Fingerprint != fingerprint) throw new ConflictException("Этот запрос уже использован для другой операции.");
        var transaction = await transactionStore.FindOwnedAsync(request.TransactionId, request.UserId, cancellationToken);
        if (transaction is null) throw new ConflictException("Эта запись уже удалена. Повторная отправка не восстановит её.");
        var category = await categoryStore.FindOwnedAsync(transaction.CategoryId, request.UserId, cancellationToken)
            ?? throw new InvalidOperationException("Transaction category is missing.");
        return TransactionMutationResult.Success(TransactionDto.From(transaction, category.Name), replayed: true);
    }
}
