using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Finance.Domain.Accounts;
using Finance.Domain.Budgets;
using Finance.Domain.Categories;
using Finance.Domain.Common;
using Finance.Domain.Ledger;
using Finance.Domain.Transactions;
using Finance.Infrastructure.Security;

namespace Finance.Infrastructure.Persistence;

internal sealed record ImportSnapshot(string Hash, int Count = 0);
internal sealed record FinancialSnapshot(string BaseCurrency, AccountData[] Accounts, CategoryData[] Categories,
    TransactionData[] Transactions, TransferData[] Transfers, BudgetData[] Budgets, RequestReceipt[] Requests, ImportSnapshot[] Imports)
{
    internal const int MaxBackupRecords = 50_000;
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) {
        NumberHandling = JsonNumberHandling.AllowReadingFromString, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() }
    };
    private sealed record LegacyReceipt(Guid RequestId, Guid TransactionId, string Fingerprint);
    internal static FinancialSnapshot FromLegacy(JsonElement root)
    {
        try { return ParseLegacy(root); }
        catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException or NullReferenceException or OverflowException or KeyNotFoundException)
        { throw Invalid(); }
    }
    private static FinancialSnapshot ParseLegacy(JsonElement root)
    {
        if (root.GetProperty("format").GetInt32() != 1) throw Invalid();
        string[] fields = ["format", "baseCurrency", "accounts", "categories", "transactions", "transfers", "budgets", "requests", "imports"];
        if (root.EnumerateObject().Any(x => !fields.Contains(x.Name))) throw Invalid();
        var transfers = Read<TransferData>("transfers");
        return new(root.GetProperty("baseCurrency").GetString()!, Read<AccountData>("accounts"),
            Read<CategoryData>("categories").Select(x => x.Version == Guid.Empty ? x with { Version = x.Id } : x).ToArray(),
            Read<TransactionData>("transactions"), transfers, Read<BudgetData>("budgets"),
            Read<LegacyReceipt>("requests").Select(x => new RequestReceipt(x.RequestId, x.TransactionId, FinancialRecordKind.Transaction, x.Fingerprint))
                .Concat(transfers.Select(x => new RequestReceipt(x.RequestId, x.Id, FinancialRecordKind.Transfer, "transfer"))).ToArray(),
            root.GetProperty("imports").EnumerateArray().Select(x => new ImportSnapshot(x.GetString()!)).ToArray());
        T[] Read<T>(string name) => root.GetProperty(name).Deserialize<T[]>(Json) ?? throw Invalid();
    }
    internal static FinancialSnapshot FromRecords(string currency, IReadOnlyList<FinancialRecord> rows, FinancialRecordProtection crypto)
    {
        T[] Read<T>(FinancialRecordKind kind) => rows.Where(x => x.Kind == kind).Select(crypto.Decrypt<T>).ToArray();
        return new(currency, Read<AccountData>(FinancialRecordKind.Account), Read<CategoryData>(FinancialRecordKind.Category),
            Read<TransactionData>(FinancialRecordKind.Transaction), Read<TransferData>(FinancialRecordKind.Transfer), Read<BudgetData>(FinancialRecordKind.Budget),
            Read<RequestReceipt>(FinancialRecordKind.Request), Read<ImportSnapshot>(FinancialRecordKind.Import));
    }
    internal IReadOnlyDictionary<Guid, decimal> Validate(Guid owner, CancellationToken ct)
    {
        if (BaseCurrency is null || BaseCurrency.Length != 3 || BaseCurrency.Any(c => c is < 'A' or > 'Z') ||
            Accounts is null || Categories is null || Transactions is null || Transfers is null || Budgets is null || Requests is null || Imports is null ||
            Accounts.Length is < 1 or > 1000 || Categories.Length > 2000 ||
            (long)Accounts.Length + Categories.Length + Transactions.Length + Transfers.Length + Budgets.Length + Requests.Length + Imports.Length > MaxBackupRecords)
            throw Invalid();
        Unique(Accounts.Select(x => (x.Id, x.Version))); Unique(Categories.Select(x => (x.Id, x.Version)));
        Unique(Transactions.Select(x => (x.Id, x.Version))); Unique(Transfers.Select(x => (x.Id, x.Version))); Unique(Budgets.Select(x => (x.Id, x.Version)));
        var accounts = Accounts.ToDictionary(x => x.Id); var categories = Categories.ToDictionary(x => x.Id);
        foreach (var a in Accounts) { ct.ThrowIfCancellationRequested(); _ = FinancialRules.CreateAccount(owner, a.Name, a.Currency, a.OpeningBalance); }
        foreach (var c in Categories) _ = Category.Create(owner, c.Name, c.Type);
        if (Accounts.GroupBy(x => x.Name.Trim().ToUpperInvariant()).Any(x => x.Count() > 1) ||
            Categories.GroupBy(x => new { Name = x.Name.Trim().ToUpperInvariant(), x.Type }).Any(x => x.Count() > 1)) throw Invalid();
        var balances = Accounts.ToDictionary(x => x.Id, x => x.OpeningBalance);
        foreach (var t in Transactions)
        {
            ct.ThrowIfCancellationRequested();
            if (!accounts.TryGetValue(t.AccountId, out var a) || !categories.TryGetValue(t.CategoryId, out var c)) throw Invalid();
            _ = FinancialRules.Create(owner, a with { IsArchived = false }, c with { IsActive = true }, t.Type, t.Amount, t.Description, t.OccurredOn, t.CreatedAtUtc);
            balances[a.Id] += FinancialRules.Signed(t);
        }
        foreach (var t in Transfers)
        {
            ct.ThrowIfCancellationRequested();
            if (!accounts.TryGetValue(t.SourceAccountId, out var source) || !accounts.TryGetValue(t.TargetAccountId, out var target) ||
                t.OccurredOn.Year is < 1900 or > 9998 || t.CancelledAtUtc?.Offset != null && t.CancelledAtUtc.Value.Offset != TimeSpan.Zero) throw Invalid();
            _ = AccountTransfer.Create(owner, t.RequestId, Account.Create(owner, source.Name, source.Currency, source.OpeningBalance, source.Id),
                Account.Create(owner, target.Name, target.Currency, target.OpeningBalance, target.Id), t.Amount, t.Description, t.CreatedAtUtc);
            if (t.Currency != source.Currency) throw Invalid();
            if (t.CancelledAtUtc is null) { balances[source.Id] -= t.Amount; balances[target.Id] += t.Amount; }
        }
        foreach (var b in Budgets)
        {
            if (!categories.TryGetValue(b.CategoryId, out var c)) throw Invalid();
            _ = MonthlyBudget.Create(owner, Category.Create(owner, c.Name, c.Type), b.Month, b.Limit);
        }
        if (Budgets.GroupBy(x => new { x.Month, x.CategoryId }).Any(x => x.Count() > 1) ||
            Requests.Any(x => x.RequestId == Guid.Empty || x.ResourceId == Guid.Empty || x.Fingerprint is null || x.Fingerprint.Length > 10_240 ||
                x.ResourceKind is not (FinancialRecordKind.Transaction or FinancialRecordKind.Transfer)) ||
            Requests.Select(x => x.RequestId).Distinct().Count() != Requests.Length ||
            Imports.Any(x => x.Hash is null || x.Hash.Length != 64 || !x.Hash.All(Uri.IsHexDigit) || x.Count is < 0 or > 1000) ||
            Imports.Select(x => x.Hash).Distinct().Count() != Imports.Length) throw Invalid();
        var receipts = Requests.ToDictionary(x => x.RequestId);
        foreach (var t in Transfers)
            if (!receipts.TryGetValue(t.RequestId, out var receipt) || receipt.ResourceId != t.Id || receipt.ResourceKind != FinancialRecordKind.Transfer) throw Invalid();
        return balances;
    }
    internal IReadOnlyList<FinancialRecord> Protect(Guid owner, FinancialRecordProtection crypto, DateTimeOffset now, bool rotate, CancellationToken ct)
    {
        var balances = Validate(owner, ct); var rows = new List<FinancialRecord>();
        Guid Version(Guid old) => rotate ? Guid.NewGuid() : old;
        foreach (var a in Accounts) { var item = a with { Balance = balances[a.Id], Version = Version(a.Version) }; Add(FinancialRecordKind.Account, a.Id, item.Version, item, now); }
        foreach (var c in Categories) { var item = c with { Version = Version(c.Version) }; Add(FinancialRecordKind.Category, c.Id, item.Version, item, now); }
        foreach (var t in Transactions) { var item = t with { Version = Version(t.Version) }; Add(FinancialRecordKind.Transaction, t.Id, item.Version, item, t.CreatedAtUtc, t.OccurredOn, t.AccountId, t.CategoryId); }
        foreach (var t in Transfers) { var item = t with { Version = Version(t.Version) }; Add(FinancialRecordKind.Transfer, t.Id, item.Version, item, t.CreatedAtUtc, t.OccurredOn, t.SourceAccountId); }
        foreach (var b in Budgets) { var item = b with { Version = Version(b.Version) }; Add(FinancialRecordKind.Budget, b.Id, item.Version, item, now, b.Month, category: b.CategoryId); }
        foreach (var r in Requests) Add(FinancialRecordKind.Request, r.RequestId, Guid.NewGuid(), r, now);
        foreach (var i in Imports) Add(FinancialRecordKind.Import, ImportId(i.Hash), Guid.NewGuid(), i, now);
        foreach (var group in Transactions.GroupBy(x => new { x.AccountId, x.CategoryId, x.OccurredOn, x.Type }))
        {
            var item = new DailyTotalData(group.Key.AccountId, group.Key.CategoryId, group.Key.OccurredOn, group.Key.Type, group.Sum(x => x.Amount), group.Count());
            Add(FinancialRecordKind.DailyTotal, FinancialBook.DailyId(owner, item.AccountId, item.CategoryId, item.Day), Guid.NewGuid(), item, now, item.Day, item.AccountId, item.CategoryId);
        }
        return rows;
        void Add<T>(FinancialRecordKind kind, Guid id, Guid version, T value, DateTimeOffset created, DateOnly? day = null, Guid? account = null, Guid? category = null)
        {
            ct.ThrowIfCancellationRequested(); var row = FinancialRecord.Create(owner, kind, id, version, created, day, account, category);
            crypto.Encrypt(row, value); rows.Add(row);
        }
    }
    private static void Unique(IEnumerable<(Guid Id, Guid Version)> values)
    { var array = values.ToArray(); if (array.Any(x => x.Id == Guid.Empty || x.Version == Guid.Empty) || array.Select(x => x.Id).Distinct().Count() != array.Length) throw Invalid(); }
    internal static Guid ImportId(string hash) => new(SHA256.HashData(Encoding.UTF8.GetBytes(hash)).AsSpan(0, 16));
    internal static DomainException Invalid() => new("Финансовая копия повреждена или содержит неподдерживаемые данные. История не изменена.");
}
