using Finance.Application.Abstractions;

namespace Finance.Application.Transactions.GetTransactions;

public sealed class GetTransactionsHandler(
    ICurrentUser currentUser,
    ITransactionStore transactionStore)
{
    public async Task<TransactionPage?> HandleAsync(
        TransactionQuery query,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return null;
        }

        query.Validate();
        var transactions = await transactionStore.ListByUserAsync(
            userId,
            query,
            cancellationToken);

        return new TransactionPage(transactions.Items.Select(TransactionDto.From).ToArray(),
            transactions.TotalCount, query.Page, query.PageSize);
    }
}
