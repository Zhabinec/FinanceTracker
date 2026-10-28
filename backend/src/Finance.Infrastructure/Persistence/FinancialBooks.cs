using Finance.Application.FinancialRecords;
using Finance.Application.Common;
using Finance.Application.Transactions.GetTransactions;
using Finance.Domain.Ledger;
using Finance.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Finance.Infrastructure.Persistence;

public sealed class FinancialBooks(FinanceDbContext db, FinancialKeyProtection keys) : IFinancialBooks
{
    public async Task<IFinancialBook> OpenAsync(Guid owner, bool write, CancellationToken ct)
    {
        var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            // Same key as legacy migration locks. A migrated workspace blocks every old write path.
            var ownerText = owner.ToString();
            if (write) await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({ownerText}, 713))", ct);
            else await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock_shared(hashtextextended({ownerText}, 713))", ct);
            var workspace = await db.FinancialWorkspaces.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == owner, ct)
                ?? throw new ConflictException("История ещё не перенесена в серверное хранилище.");
            var key = await db.ManagedFinancialKeys.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == owner && x.VaultId == workspace.VaultId, ct)
                ?? throw new ConflictException("Финансовый ключ недоступен.");
            return new FinancialBook(db, transaction, new FinancialRecordProtection(owner, keys.Unprotect(key)), workspace, write);
        }
        catch { await transaction.DisposeAsync(); throw; }
    }
}

internal sealed class FinancialBook(FinanceDbContext db, IDbContextTransaction transaction, FinancialRecordProtection protection,
    FinancialWorkspace workspace, bool writable) : IFinancialBook
{
    public string BaseCurrency => workspace.BaseCurrency;
    private Guid Owner => workspace.UserId;
    private IQueryable<FinancialRecord> Rows(FinancialRecordKind kind) => db.FinancialRecords.Where(x => x.UserId == Owner && x.Kind == kind);
    private async Task<T?> Read<T>(FinancialRecordKind kind, Guid id, CancellationToken ct) where T : class
    {
        var row = await Rows(kind).SingleOrDefaultAsync(x => x.Id == id, ct);
        return row is null ? null : protection.Decrypt<T>(row);
    }
    public Task<AccountData?> AccountAsync(Guid id, CancellationToken ct) => Read<AccountData>(FinancialRecordKind.Account, id, ct);
    public Task<CategoryData?> CategoryAsync(Guid id, CancellationToken ct) => Read<CategoryData>(FinancialRecordKind.Category, id, ct);
    public Task<TransactionData?> TransactionAsync(Guid id, CancellationToken ct) => Read<TransactionData>(FinancialRecordKind.Transaction, id, ct);
    public Task<RequestReceipt?> ReceiptAsync(Guid id, CancellationToken ct) => Read<RequestReceipt>(FinancialRecordKind.Request, id, ct);
    public async Task<IReadOnlyList<AccountData>> AccountsAsync(CancellationToken ct) =>
        (await Rows(FinancialRecordKind.Account).ToListAsync(ct)).Select(protection.Decrypt<AccountData>).ToArray();
    public async Task<IReadOnlyList<CategoryData>> CategoriesAsync(CancellationToken ct) =>
        (await Rows(FinancialRecordKind.Category).ToListAsync(ct)).Select(protection.Decrypt<CategoryData>).ToArray();

    public async Task<(IReadOnlyList<TransactionData> Items, int Count)> TransactionsAsync(TransactionQuery query, CancellationToken ct)
    {
        query.Validate();
        var rows = Rows(FinancialRecordKind.Transaction).AsNoTracking();
        if (query.From is { } from) rows = rows.Where(x => x.OccurredOn >= from);
        if (query.To is { } to) rows = rows.Where(x => x.OccurredOn <= to);
        if (query.AccountId is { } account) rows = rows.Where(x => x.AccountId == account);
        if (query.CategoryId is { } category) rows = rows.Where(x => x.CategoryId == category);
        // Type filtering is converted to indexed category IDs; the type itself stays encrypted.
        if (query.Type is { } type)
        {
            var ids = (await CategoriesAsync(ct)).Where(x => x.Type == type).Select(x => x.Id).ToArray();
            rows = rows.Where(x => ids.Contains(x.CategoryId!.Value));
        }
        var count = await rows.CountAsync(ct);
        if (query.Sort is "amountAsc" or "amountDesc")
        {
            // Ciphertext cannot be ordered by its numeric value in SQL. Decrypt only the filtered
            // result on the trusted server, never an entire client-side financial document.
            var values = (await rows.ToListAsync(ct)).Select(protection.Decrypt<TransactionData>);
            var byAmount = query.Sort == "amountAsc" ? values.OrderBy(x => x.Amount) : values.OrderByDescending(x => x.Amount);
            return (byAmount.ThenByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id)
                .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToArray(), count);
        }
        var ordered = query.Sort == "dateAsc" ? rows.OrderBy(x => x.OccurredOn) : rows.OrderByDescending(x => x.OccurredOn);
        var page = await ordered.ThenByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return (page.Select(protection.Decrypt<TransactionData>).ToArray(), count);
    }
    private void RequireWrite() { if (!writable) throw new InvalidOperationException("Cannot change a read-only financial book."); }
    private async Task Save<T>(FinancialRecordKind kind, Guid id, Guid version, T payload, DateTimeOffset created,
        CancellationToken ct, DateOnly? day = null, Guid? account = null, Guid? category = null)
    {
        RequireWrite();
        var row = await Rows(kind).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (row is null)
        {
            row = FinancialRecord.Create(Owner, kind, id, version, created, day, account, category);
            db.FinancialRecords.Add(row);
        }
        else row.Revise(version, day, account, category);
        protection.Encrypt(row, payload);
    }
    private async Task ChangeBalance(Guid accountId, decimal delta, CancellationToken ct)
    {
        var account = await AccountAsync(accountId, ct) ?? throw new FinancialRecordNotFoundException();
        var changed = account with { Balance = checked(account.Balance + delta), Version = Guid.NewGuid() };
        await Save(FinancialRecordKind.Account, accountId, changed.Version, changed, DateTimeOffset.UtcNow, ct);
    }
    public async Task SaveTransactionAsync(TransactionData value, TransactionData? previous, RequestReceipt? receipt, CancellationToken ct)
    {
        RequireWrite();
        if (previous is not null) await ChangeBalance(previous.AccountId, -FinancialRules.Signed(previous), ct);
        await ChangeBalance(value.AccountId, FinancialRules.Signed(value), ct);
        await Save(FinancialRecordKind.Transaction, value.Id, value.Version, value, value.CreatedAtUtc, ct,
            value.OccurredOn, value.AccountId, value.CategoryId);
        if (receipt is not null) await Save(FinancialRecordKind.Request, receipt.RequestId, Guid.NewGuid(), receipt, value.CreatedAtUtc, ct);
    }
    public async Task DeleteTransactionAsync(TransactionData value, CancellationToken ct)
    {
        RequireWrite();
        await ChangeBalance(value.AccountId, -FinancialRules.Signed(value), ct);
        var row = await Rows(FinancialRecordKind.Transaction).SingleAsync(x => x.Id == value.Id, ct);
        db.FinancialRecords.Remove(row);
        // Keep the receipt: retrying an old create must never resurrect a deleted entry.
    }
    public async Task CommitAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new ConflictException("Финансовая запись уже изменена. Обновите данные."); }
    }
    public async ValueTask DisposeAsync()
    {
        protection.Dispose();
        await transaction.DisposeAsync();
    }
}
