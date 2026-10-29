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
    Task SaveAccountAsync(AccountData value, CancellationToken ct);
    Task SaveCategoryAsync(CategoryData value, CancellationToken ct);
    Task<IReadOnlyList<BudgetData>> BudgetsAsync(DateOnly month, CancellationToken ct);
    Task<BudgetData?> BudgetAsync(Guid id, CancellationToken ct);
    Task SaveBudgetAsync(BudgetData value, CancellationToken ct);
    Task DeleteBudgetAsync(BudgetData value, CancellationToken ct);
    Task<IReadOnlyList<DailyTotalData>> DailyTotalsAsync(DateOnly from, DateOnly to, CancellationToken ct);
    Task<TransferData?> TransferAsync(Guid id, CancellationToken ct);
    Task<TransferData?> TransferRequestAsync(Guid requestId, CancellationToken ct);
    Task<(IReadOnlyList<TransferData> Items, int Count)> TransfersAsync(int page, int pageSize, CancellationToken ct);
    Task SaveTransferAsync(TransferData value, bool cancel, CancellationToken ct);
    Task<bool> ImportedAsync(string fingerprint, CancellationToken ct);
    Task<Guid> SaveImportAsync(string fingerprint, int count, CancellationToken ct);
    Task<(IReadOnlyList<TransactionData> Items, int Count)> TransactionsAsync(TransactionQuery query, CancellationToken ct);
    Task SaveTransactionAsync(TransactionData value, TransactionData? previous, RequestReceipt? receipt, CancellationToken ct);
    Task DeleteTransactionAsync(TransactionData value, CancellationToken ct);
    Task CommitAsync(CancellationToken ct);
    Task<EncryptedFinancialBackup> BackupAsync(CancellationToken ct);
    FinancialBackupPreview PreviewBackup(System.Text.Json.JsonElement backup, CancellationToken ct);
    Task RestoreBackupAsync(System.Text.Json.JsonElement backup, long expectedRevision, CancellationToken ct);
}
public interface IFinancialBooks
{
    Task<IFinancialBook> OpenAsync(Guid owner, bool write, CancellationToken ct);
}
