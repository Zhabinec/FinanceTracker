using System.Text.Json;
using System.Text.Json.Serialization;
using Finance.Application.Abstractions;
using Finance.Application.Common;
using Finance.Application.FinancialRecords;
using Finance.Domain.Ledger;
using Finance.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace Finance.Infrastructure.Persistence;

public sealed class FinancialBookMigration(FinanceDbContext db, FinancialKeyProtection keys, IClock clock) : IFinancialBookMigration
{
    private sealed record LegacyReceipt(Guid RequestId, Guid TransactionId, string Fingerprint);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() }
    };
    public async Task<FinancialBookStatus> StatusAsync(Guid owner, CancellationToken ct)
    {
        var workspace = await db.FinancialWorkspaces.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == owner, ct);
        return new(workspace is not null, workspace?.SourceRevision, workspace?.BaseCurrency);
    }
    public async Task<FinancialBookStatus> MigrateAsync(Guid owner, long expectedRevision, CancellationToken ct)
    {
        if (expectedRevision < 1) throw new Finance.Domain.Common.DomainException("Нужна текущая ревизия хранилища.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var ownerText = owner.ToString();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({ownerText}, 713))", ct);
        var existing = await StatusAsync(owner, ct);
        if (existing.Ready)
        {
            if (existing.SourceRevision != expectedRevision) throw new ConflictException("История уже перенесена из другой ревизии.");
            await transaction.CommitAsync(ct);
            return existing;
        }
        var vault = await db.PrivateFinancialVaults.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == owner, ct)
            ?? throw new ConflictException("Исходное хранилище не найдено.");
        if (!vault.IsActive || vault.Revision != expectedRevision) throw new ConflictException("История изменилась. Повторите перенос.");
        var managed = await db.ManagedFinancialKeys.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == owner && x.VaultId == vault.Id, ct)
            ?? throw new ConflictException("Сначала подключите серверное хранение финансового ключа.");
        var code = keys.Unprotect(managed);
        using var document = FinancialKeyProtection.DecryptDocument(code, vault);
        var root = document.RootElement;
        if (root.GetProperty("format").GetInt32() != 1) throw new ConflictException("Неизвестный формат истории.");
        string[] fields = ["format", "baseCurrency", "accounts", "categories", "transactions", "transfers", "budgets", "requests", "imports"];
        if (root.EnumerateObject().Any(x => !fields.Contains(x.Name))) throw new ConflictException("История содержит неподдерживаемые разделы. Перенос отменён без изменений.");
        var currency = root.GetProperty("baseCurrency").GetString() ?? throw new ConflictException("Не задана валюта.");
        using var protection = new FinancialRecordProtection(owner, code);
        var now = clock.UtcNow;
        var accounts = Read<AccountData>(root, "accounts").ToDictionary(x => x.Id);
        var categories = Read<CategoryData>(root, "categories").Select(x => x.Version == Guid.Empty ? x with { Version = x.Id } : x).ToDictionary(x => x.Id);
        var entries = Read<TransactionData>(root, "transactions");
        if (accounts.Count == 0) throw new ConflictException("В истории нет счёта.");
        if (accounts.Values.GroupBy(x => x.Name.Trim().ToUpperInvariant()).Any(x => x.Count() > 1) ||
            categories.Values.GroupBy(x => new { Name = x.Name.Trim().ToUpperInvariant(), x.Type }).Any(x => x.Count() > 1))
            throw new ConflictException("В истории повторяются названия счетов или категорий. Исправьте их перед переносом.");
        var balances = accounts.ToDictionary(x => x.Key, x => x.Value.OpeningBalance);
        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            if (!accounts.TryGetValue(entry.AccountId, out var account) || !categories.TryGetValue(entry.CategoryId, out var category))
                throw new ConflictException("Повреждены связи финансовых записей.");
            // Historical inactive categories and archived accounts are valid, but amounts and links still must validate.
            _ = FinancialRules.Create(owner, account with { IsArchived = false }, category with { IsActive = true },
                entry.Type, entry.Amount, entry.Description, entry.OccurredOn, entry.CreatedAtUtc);
            balances[entry.AccountId] = checked(balances[entry.AccountId] + FinancialRules.Signed(entry));
            Add(FinancialRecordKind.Transaction, entry.Id, entry.Version, entry, entry.CreatedAtUtc,
                entry.OccurredOn, entry.AccountId, entry.CategoryId);
        }
        foreach (var transfer in root.GetProperty("transfers").EnumerateArray())
        {
            var source = transfer.GetProperty("sourceAccountId").GetGuid();
            var target = transfer.GetProperty("targetAccountId").GetGuid();
            var amount = Decimal(transfer.GetProperty("amount"));
            var transferCurrency = transfer.GetProperty("currency").GetString();
            if (source == target || !accounts.ContainsKey(source) || !accounts.ContainsKey(target) || amount <= 0 ||
                accounts[source].Currency != transferCurrency || accounts[target].Currency != transferCurrency || decimal.Round(amount, 2) != amount ||
                amount > Finance.Domain.Transactions.Transaction.MaxAmount)
                throw new ConflictException("Повреждён перевод между счетами.");
            if (transfer.GetProperty("cancelledAtUtc").ValueKind == JsonValueKind.Null)
            { balances[source] -= amount; balances[target] += amount; }
            Add(FinancialRecordKind.Transfer, transfer.GetProperty("id").GetGuid(), transfer.GetProperty("version").GetGuid(),
                transfer.Deserialize<TransferData>(Json)!, transfer.GetProperty("createdAtUtc").GetDateTimeOffset(),
                DateOnly.ParseExact(transfer.GetProperty("occurredOn").GetString()!, "yyyy-MM-dd"), source);
            Add(FinancialRecordKind.Request, transfer.GetProperty("requestId").GetGuid(), Guid.NewGuid(),
                new RequestReceipt(transfer.GetProperty("requestId").GetGuid(), transfer.GetProperty("id").GetGuid(), FinancialRecordKind.Transfer, "transfer"), now);
        }
        foreach (var group in entries.GroupBy(x => new { x.AccountId, x.CategoryId, x.OccurredOn, x.Type }))
        {
            var daily = new DailyTotalData(group.Key.AccountId, group.Key.CategoryId, group.Key.OccurredOn, group.Key.Type, group.Sum(x => x.Amount), group.Count());
            Add(FinancialRecordKind.DailyTotal, FinancialBook.DailyId(owner, daily.AccountId, daily.CategoryId, daily.Day),
                Guid.NewGuid(), daily, now, daily.Day, daily.AccountId, daily.CategoryId);
        }
        foreach (var account in accounts.Values)
        {
            _ = FinancialRules.CreateAccount(owner, account.Name, account.Currency, account.OpeningBalance);
            Add(FinancialRecordKind.Account, account.Id, account.Version, account with { Balance = balances[account.Id] }, now);
        }
        foreach (var category in categories.Values)
        {
            _ = Finance.Domain.Categories.Category.Create(owner, category.Name, category.Type);
            Add(FinancialRecordKind.Category, category.Id, category.Version, category, now);
        }
        foreach (var budget in root.GetProperty("budgets").EnumerateArray())
        {
            var categoryId = budget.GetProperty("categoryId").GetGuid();
            var month = DateOnly.ParseExact(budget.GetProperty("month").GetString()!, "yyyy-MM-dd");
            var limit = Decimal(budget.GetProperty("limit"));
            if (!categories.TryGetValue(categoryId, out var category) || category.Type != Finance.Domain.Transactions.TransactionType.Expense ||
                month.Day != 1 || limit <= 0 || decimal.Round(limit, 2) != limit || limit > Finance.Domain.Transactions.Transaction.MaxAmount)
                throw new ConflictException("Повреждён бюджет.");
            Add(FinancialRecordKind.Budget, budget.GetProperty("id").GetGuid(), budget.GetProperty("version").GetGuid(), budget.Deserialize<BudgetData>(Json)!, now, month, category: categoryId);
        }
        foreach (var receipt in Read<LegacyReceipt>(root, "requests"))
            Add(FinancialRecordKind.Request, receipt.RequestId, Guid.NewGuid(),
                new RequestReceipt(receipt.RequestId, receipt.TransactionId, FinancialRecordKind.Transaction, receipt.Fingerprint), now);
        foreach (var hash in root.GetProperty("imports").EnumerateArray().Select(x => x.GetString()!))
        {
            if (hash.Length != 64 || !hash.All(Uri.IsHexDigit)) throw new ConflictException("Повреждена квитанция импорта.");
            var id = new Guid(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(hash)).AsSpan(0, 16));
            Add(FinancialRecordKind.Import, id, Guid.NewGuid(), new { hash }, now);
        }
        db.FinancialWorkspaces.Add(FinancialWorkspace.Create(owner, vault.Id, vault.Revision, currency, now));
        await db.SaveChangesAsync(ct);
        // Read back every persisted payload before committing the cutover marker.
        var persisted = await db.FinancialRecords.AsNoTracking().Where(x => x.UserId == owner).ToListAsync(ct);
        foreach (var row in persisted) { ct.ThrowIfCancellationRequested(); _ = protection.Decrypt<JsonElement>(row); }
        await transaction.CommitAsync(ct);
        return new(true, vault.Revision, currency);

        void Add<T>(FinancialRecordKind kind, Guid id, Guid version, T value, DateTimeOffset created,
            DateOnly? day = null, Guid? account = null, Guid? category = null)
        {
            var row = FinancialRecord.Create(owner, kind, id, version, created, day, account, category);
            protection.Encrypt(row, value);
            db.FinancialRecords.Add(row);
        }
    }
    private static T[] Read<T>(JsonElement root, string name) => root.GetProperty(name).Deserialize<T[]>(Json)
        ?? throw new ConflictException("Повреждена история.");
    private static decimal Decimal(JsonElement element) => element.ValueKind == JsonValueKind.String
        ? decimal.Parse(element.GetString()!, System.Globalization.CultureInfo.InvariantCulture) : element.GetDecimal();
}
