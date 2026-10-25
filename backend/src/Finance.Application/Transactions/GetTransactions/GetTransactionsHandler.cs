using Finance.Application.Abstractions;

namespace Finance.Application.Transactions.GetTransactions;

public sealed class GetTransactionsHandler(
    ICurrentUser currentUser,
    ITransactionStore transactionStore)
{
    public async Task<IReadOnlyList<TransactionDto>?> HandleAsync(
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return null;
        }

        var transactions = await transactionStore.ListByUserAsync(
            userId,
            cancellationToken);

        return transactions.Select(TransactionDto.From).ToArray();
    }
}
