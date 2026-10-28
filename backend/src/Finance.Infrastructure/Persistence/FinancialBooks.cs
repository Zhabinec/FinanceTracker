using System.Data;
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
        var transaction = await db.Database.BeginTransactionAsync(write ? IsolationLevel.ReadCommitted : IsolationLevel.RepeatableRead, ct);
        try
        {
            // Same key as legacy migration locks. A migrated workspace blocks every old write path.
            var ownerText = owner.ToString();
            if (write) await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({ownerText}, 713))", ct);
            // Readers use a consistent MVCC snapshot and do not block adding a new expense.
            var workspace = await db.FinancialWorkspaces.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == owner, ct)
                ?? throw new ConflictException("История ещё не перенесена в серверное хранилище.");
            var key = await db.ManagedFinancialKeys.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == owner && x.VaultId == workspace.VaultId, ct)
                ?? throw new ConflictException("Финансовый ключ недоступен.");
            return new FinancialBook(db, transaction, new FinancialRecordProtection(owner, keys.Unprotect(key)), workspace, write);
        }
        catch { await transaction.DisposeAsync(); throw; }
    }
}

internal sealed partial class FinancialBook(FinanceDbContext db, IDbContextTransaction transaction, FinancialRecordProtection protection,
    FinancialWorkspace workspace, bool writable) : IFinancialBook
{
    private bool committed;
    public string BaseCurrency => workspace.BaseCurrency;
    private Guid Owner => workspace.UserId;
    private IQueryable<FinancialRecord> Rows(FinancialRecordKind kind) => db.FinancialRecords.Where(x => x.UserId == Owner && x.Kind == kind);
    private Task<FinancialRecord?> FindRow(FinancialRecordKind kind, Guid id, CancellationToken ct)
    {
        var local = db.FinancialRecords.Local.FirstOrDefault(x => x.UserId == Owner && x.Kind == kind && x.Id == id);
        return local is null ? Rows(kind).SingleOrDefaultAsync(x => x.Id == id, ct) : Task.FromResult<FinancialRecord?>(local);
    }
    private async Task<T?> Read<T>(FinancialRecordKind kind, Guid id, CancellationToken ct) where T : class
    {
        var row = await FindRow(kind, id, ct);
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
            var bestFirst = Comparer<TransactionData>.Create((left, right) => {
                var amount = left.Amount.CompareTo(right.Amount);
                if (amount != 0) return query.Sort == "amountAsc" ? amount : -amount;
                var created = right.CreatedAtUtc.CompareTo(left.CreatedAtUtc);
                return created != 0 ? created : left.Id.CompareTo(right.Id);
            });
            var offset = (query.Page - 1) * query.PageSize;
            var capacity = Math.Min(count, offset + query.PageSize);
            var top = new PriorityQueue<TransactionData, TransactionData>(Comparer<TransactionData>.Create((a, b) => bestFirst.Compare(b, a)));
            await foreach (var row in rows.AsAsyncEnumerable().WithCancellation(ct))
            {
                var value = protection.Decrypt<TransactionData>(row);
                top.Enqueue(value, value);
                if (top.Count > capacity) top.Dequeue();
            }
            return (top.UnorderedItems.Select(x => x.Element).Order(bestFirst).Skip(offset).Take(query.PageSize).ToArray(), count);
        }
        var ordered = query.Sort == "dateAsc" ? rows.OrderBy(x => x.OccurredOn) : rows.OrderByDescending(x => x.OccurredOn);
        var page = await ordered.ThenByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return (page.Select(protection.Decrypt<TransactionData>).ToArray(), count);
    }
    private void RequireWrite() { if (!writable) throw new InvalidOperationException("Cannot change a read-only financial book."); }
    private async Task Save<T>(FinancialRecordKind kind, Guid id, Guid version, T payload, DateTimeOffset created,
        CancellationToken ct, DateOnly? day = null, Guid? account = null, Guid? category = null, bool isNew = false)
    {
        RequireWrite();
        var row = isNew ? null : await FindRow(kind, id, ct);
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
        if (previous is not null) { await ChangeBalance(previous.AccountId, -FinancialRules.Signed(previous), ct); await ChangeDaily(previous, -1, ct); }
        await ChangeBalance(value.AccountId, FinancialRules.Signed(value), ct);
        await ChangeDaily(value, 1, ct);
        await Save(FinancialRecordKind.Transaction, value.Id, value.Version, value, value.CreatedAtUtc, ct,
            value.OccurredOn, value.AccountId, value.CategoryId, isNew: previous is null);
        if (receipt is not null) await Save(FinancialRecordKind.Request, receipt.RequestId, Guid.NewGuid(), receipt, value.CreatedAtUtc, ct, isNew: true);
    }
    public async Task DeleteTransactionAsync(TransactionData value, CancellationToken ct)
    {
        RequireWrite();
        await ChangeBalance(value.AccountId, -FinancialRules.Signed(value), ct);
        await ChangeDaily(value, -1, ct);
        var row = await Rows(FinancialRecordKind.Transaction).SingleAsync(x => x.Id == value.Id, ct);
        db.FinancialRecords.Remove(row);
        // Keep the receipt: retrying an old create must never resurrect a deleted entry.
    }
    public async Task CommitAsync(CancellationToken ct)
    {
        if (writable)
            foreach (var entry in db.ChangeTracker.Entries<FinancialRecord>().Where(x =>
                x.Entity.UserId == Owner && x.Entity.Kind == FinancialRecordKind.DailyTotal &&
                x.State is EntityState.Added or EntityState.Modified).ToArray())
                if (protection.Decrypt<DailyTotalData>(entry.Entity).Count == 0) db.FinancialRecords.Remove(entry.Entity);
        try { await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); committed = true; }
        catch (DbUpdateConcurrencyException) { throw new ConflictException("Финансовая запись уже изменена. Обновите данные."); }
    }
    public async ValueTask DisposeAsync()
    {
        protection.Dispose();
        await transaction.DisposeAsync();
        if (!committed) db.ChangeTracker.Clear();
    }
}
