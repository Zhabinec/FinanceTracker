using Finance.Domain.Ledger;
using Finance.Application.Transactions.GetTransactions;

namespace Finance.Application.FinancialRecords;

// One owner-scoped unit of work, not a generic repository. Writes commit only after the use case succeeds.
public interface IFinancialBook : IAsyncDisposable
{
    string BaseCurrency { get; }
    Task<AccountData?> AccountAsync(Guid id, CancellationToken ct);
    Task<CategoryData?> CategoryAsync(Guid id, CancellationToken ct);
    Task<TransactionData?> TransactionAsync(Guid id, CancellationToken ct);
    Task<RequestReceipt?> ReceiptAsync(Guid requestId, CancellationToken ct);
    Task<IReadOnlyList<AccountData>> AccountsAsync(CancellationToken ct);
    Task<IReadOnlyList<CategoryData>> CategoriesAsync(CancellationToken ct);
    Task<(IReadOnlyList<TransactionData> Items, int Count)> TransactionsAsync(TransactionQuery query, CancellationToken ct);
    Task SaveTransactionAsync(TransactionData value, TransactionData? previous, RequestReceipt? receipt, CancellationToken ct);
    Task DeleteTransactionAsync(TransactionData value, CancellationToken ct);
    Task CommitAsync(CancellationToken ct);
}
public interface IFinancialBooks
{
    Task<IFinancialBook> OpenAsync(Guid owner, bool write, CancellationToken ct);
}
