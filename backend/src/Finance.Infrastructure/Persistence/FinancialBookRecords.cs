using System.Security.Cryptography;
using System.Text;
using Finance.Domain.Ledger;
using Microsoft.EntityFrameworkCore;

namespace Finance.Infrastructure.Persistence;

internal sealed partial class FinancialBook
{
    private sealed record ImportReceipt(string Hash, int Count);
    private static Guid ImportId(string hash) => new(SHA256.HashData(Encoding.UTF8.GetBytes(hash)).AsSpan(0, 16));
    public async Task<bool> ImportedAsync(string fingerprint, CancellationToken ct)
    {
        var receipt = await Read<ImportReceipt>(FinancialRecordKind.Import, ImportId(fingerprint), ct);
        if (receipt is not null && receipt.Hash != fingerprint) throw new InvalidOperationException("Import receipt identity mismatch.");
        return receipt is not null;
    }
    public async Task<Guid> SaveImportAsync(string fingerprint, int count, CancellationToken ct)
    {
        var id = ImportId(fingerprint);
        await Save(FinancialRecordKind.Import, id, Guid.NewGuid(), new ImportReceipt(fingerprint, count), DateTimeOffset.UtcNow, ct, isNew: true);
        return id;
    }
    internal static Guid DailyId(Guid owner, Guid account, Guid category, DateOnly day) => new(SHA256.HashData(
        Encoding.UTF8.GetBytes(FormattableString.Invariant($"FinanceTracker:daily:v1:{owner}:{account}:{category}:{day:yyyy-MM-dd}"))).AsSpan(0, 16));
    private async Task ChangeDaily(TransactionData entry, int direction, CancellationToken ct)
    {
        var id = DailyId(Owner, entry.AccountId, entry.CategoryId, entry.OccurredOn);
        var row = await FindRow(FinancialRecordKind.DailyTotal, id, ct);
        var current = row is null ? new DailyTotalData(entry.AccountId, entry.CategoryId, entry.OccurredOn, entry.Type, 0, 0)
            : protection.Decrypt<DailyTotalData>(row);
        if (current.AccountId != entry.AccountId || current.CategoryId != entry.CategoryId || current.Day != entry.OccurredOn || current.Type != entry.Type)
            throw new InvalidOperationException("Daily total identity mismatch.");
        var changed = current with { Amount = current.Amount + entry.Amount * direction, Count = current.Count + direction };
        if (changed.Amount < 0 || changed.Count < 0 || changed.Count == 0 && changed.Amount != 0)
            throw new InvalidOperationException("Daily total is inconsistent.");
        // Keep zero buckets until commit so an edit within the same bucket can update the tracked row.
        await Save(FinancialRecordKind.DailyTotal, id, Guid.NewGuid(), changed, DateTimeOffset.UtcNow, ct,
            entry.OccurredOn, entry.AccountId, entry.CategoryId);
    }
    public async Task<IReadOnlyList<DailyTotalData>> DailyTotalsAsync(DateOnly from, DateOnly to, CancellationToken ct) =>
        (await Rows(FinancialRecordKind.DailyTotal).AsNoTracking().Where(x => x.OccurredOn >= from && x.OccurredOn <= to)
            .ToListAsync(ct)).Select(protection.Decrypt<DailyTotalData>).Where(x => x.Count > 0).ToArray();
    public Task SaveAccountAsync(AccountData value, CancellationToken ct) =>
        Save(FinancialRecordKind.Account, value.Id, value.Version, value, DateTimeOffset.UtcNow, ct);
    public Task SaveCategoryAsync(CategoryData value, CancellationToken ct) =>
        Save(FinancialRecordKind.Category, value.Id, value.Version, value, DateTimeOffset.UtcNow, ct);
    public Task<BudgetData?> BudgetAsync(Guid id, CancellationToken ct) => Read<BudgetData>(FinancialRecordKind.Budget, id, ct);
    public async Task<IReadOnlyList<BudgetData>> BudgetsAsync(DateOnly month, CancellationToken ct) =>
        (await Rows(FinancialRecordKind.Budget).Where(x => x.OccurredOn == month).ToListAsync(ct)).Select(protection.Decrypt<BudgetData>).ToArray();
    public Task SaveBudgetAsync(BudgetData value, CancellationToken ct) =>
        Save(FinancialRecordKind.Budget, value.Id, value.Version, value, DateTimeOffset.UtcNow, ct, value.Month, category: value.CategoryId);
    public async Task DeleteBudgetAsync(BudgetData value, CancellationToken ct)
    {
        RequireWrite();
        db.FinancialRecords.Remove(await Rows(FinancialRecordKind.Budget).SingleAsync(x => x.Id == value.Id, ct));
    }
    public Task<TransferData?> TransferAsync(Guid id, CancellationToken ct) => Read<TransferData>(FinancialRecordKind.Transfer, id, ct);
    public async Task<TransferData?> TransferRequestAsync(Guid requestId, CancellationToken ct)
    {
        var receipt = await ReceiptAsync(requestId, ct);
        if (receipt is not null && receipt.ResourceKind != FinancialRecordKind.Transfer) throw new Finance.Application.Common.ConflictException("Запрос уже использован для другой операции.");
        return receipt is null ? null : await TransferAsync(receipt.ResourceId, ct);
    }
    public async Task<(IReadOnlyList<TransferData> Items, int Count)> TransfersAsync(int page, int pageSize, CancellationToken ct)
    {
        var rows = Rows(FinancialRecordKind.Transfer).AsNoTracking();
        var count = await rows.CountAsync(ct);
        var items = await rows.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return (items.Select(protection.Decrypt<TransferData>).ToArray(), count);
    }
    public async Task SaveTransferAsync(TransferData value, bool cancel, CancellationToken ct)
    {
        RequireWrite();
        var sign = cancel ? -1 : 1;
        await ChangeBalance(value.SourceAccountId, -value.Amount * sign, ct);
        await ChangeBalance(value.TargetAccountId, value.Amount * sign, ct);
        await Save(FinancialRecordKind.Transfer, value.Id, value.Version, value, value.CreatedAtUtc, ct, value.OccurredOn, value.SourceAccountId);
        if (!cancel)
            await Save(FinancialRecordKind.Request, value.RequestId, Guid.NewGuid(),
                new RequestReceipt(value.RequestId, value.Id, FinancialRecordKind.Transfer, "transfer"), value.CreatedAtUtc, ct);
    }
}
