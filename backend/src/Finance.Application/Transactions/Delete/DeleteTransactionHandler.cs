using Finance.Application.Abstractions;

namespace Finance.Application.Transactions.Delete;

public enum DeleteTransactionStatus
{
    Success,
    NotFound,
    Unauthorized
}

public sealed class DeleteTransactionHandler(
    ICurrentUser currentUser,
    ITransactionStore transactionStore)
{
    public async Task<DeleteTransactionStatus> HandleAsync(
        Guid transactionId,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return DeleteTransactionStatus.Unauthorized;
        }

        var transaction = await transactionStore.FindOwnedAsync(
            transactionId,
            userId,
            cancellationToken);
        if (transaction is null)
        {
            return DeleteTransactionStatus.NotFound;
        }

        transactionStore.Remove(transaction);
        await transactionStore.SaveChangesAsync(cancellationToken);
        return DeleteTransactionStatus.Success;
    }
}
