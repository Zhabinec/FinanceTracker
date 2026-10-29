using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Finance.Application.Abstractions;
using Finance.Application.Common;
using Finance.Application.Imports;
using Finance.Application.Messaging;
using Finance.Domain.Common;
using Finance.Domain.Ledger;
using Finance.Domain.Transactions;

namespace Finance.Application.FinancialRecords;

public sealed record PreviewFinancialCsv(ImportCommand Input) : IRequest<ImportPreview>, IAuthenticatedRequest;
public sealed record ApplyFinancialCsv(ImportCommand Input) : IRequest<ImportResult>, IAuthenticatedRequest;
public sealed class FinancialImportHandler(ICurrentUser current, IFinancialBooks books, IClock clock)
    : IRequestHandler<PreviewFinancialCsv, ImportPreview>, IRequestHandler<ApplyFinancialCsv, ImportResult>
{
    private Guid Owner => current.UserId ?? throw new UnauthorizedAccessException();
    public async Task<ImportPreview> HandleAsync(PreviewFinancialCsv request, CancellationToken ct)
    {
        await using var book = await books.OpenAsync(Owner, false, ct);
        var prepared = await Prepare(book, request.Input, ct);
        await book.CommitAsync(ct); return prepared.Preview;
    }
    public async Task<ImportResult> HandleAsync(ApplyFinancialCsv request, CancellationToken ct)
    {
        await using var book = await books.OpenAsync(Owner, true, ct);
        var prepared = await Prepare(book, request.Input, ct);
        if (prepared.Preview.AlreadyImported) throw new ConflictException("Этот CSV уже импортирован.");
        if (!prepared.Preview.CanApply) throw new DomainException("Исправьте ошибки предпросмотра. Ни одна операция не импортирована.");
        foreach (var item in prepared.Entries) { ct.ThrowIfCancellationRequested(); await book.SaveTransactionAsync(item, null, null, ct); }
        var id = await book.SaveImportAsync(prepared.Hash, prepared.Entries.Count, ct);
        await book.CommitAsync(ct); return new(id, prepared.Entries.Count);
    }
    private sealed record Prepared(string Hash, ImportPreview Preview, IReadOnlyList<TransactionData> Entries);
    private async Task<Prepared> Prepare(IFinancialBook book, ImportCommand input, CancellationToken ct)
    {
        if (input.Content is null || input.Delimiter is not ("," or ";" or "\t") || input.Mapping is null || !Enum.IsDefined(input.DefaultType))
            throw new DomainException("Проверьте файл, разделитель и тип операций.");
        var rows = CsvReader.Read(input.Content, input.Delimiter[0]);
        var columns = new int?[] { input.Mapping.Date, input.Mapping.Amount, input.Mapping.Category, input.Mapping.Description, input.Mapping.Type };
        if (columns.Any(x => x.HasValue && (x < 0 || x >= rows[0].Length)) ||
            columns.Where(x => x.HasValue).Distinct().Count() != columns.Count(x => x.HasValue))
            throw new DomainException("Для каждого поля выберите отдельную существующую колонку.");
        var account = await book.AccountAsync(input.AccountId, ct) ?? throw new FinancialRecordNotFoundException();
        if (account.IsArchived) throw new DomainException("Выберите активный счёт.");
        var categories = await book.CategoriesAsync(ct);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input.Content.TrimStart('\uFEFF').Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n'))));
        var duplicate = await book.ImportedAsync(hash, ct);
        var preview = new List<ImportRow>(); var entries = new List<TransactionData>(); var now = clock.UtcNow;
        for (var i = 1; i < rows.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var row = rows[i]; string Cell(int? index) => index.HasValue && index.Value < row.Length ? row[index.Value].Trim() : "";
            var errors = new List<string>();
            if (row.Length != rows[0].Length) errors.Add("Число колонок отличается от заголовка.");
            var dateText = Cell(input.Mapping.Date);
            var validDate = DateOnly.TryParseExact(dateText, ["yyyy-MM-dd", "dd.MM.yyyy", "dd/MM/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                && date.Year is >= 1900 and <= 9998;
            if (!validDate) errors.Add("Дата: ГГГГ-ММ-ДД, ДД.ММ.ГГГГ или ДД/ММ/ГГГГ.");
            var amountText = Cell(input.Mapping.Amount).Replace(" ", "").Replace("\u00a0", "").Replace("\u202f", "").Replace(',', '.');
            var validAmount = decimal.TryParse(amountText, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)
                && amount > 0 && amount <= Transaction.MaxAmount && decimal.Round(amount, 2) == amount;
            if (!validAmount) errors.Add("Нужна положительная сумма с точностью до копеек.");
            var type = input.DefaultType;
            if (input.Mapping.Type.HasValue)
                switch (Cell(input.Mapping.Type).ToLowerInvariant())
                {
                    case "income": case "доход": type = TransactionType.Income; break;
                    case "expense": case "расход": type = TransactionType.Expense; break;
                    default: errors.Add("Тип: Income/Доход или Expense/Расход."); break;
                }
            var name = Cell(input.Mapping.Category);
            var category = categories.SingleOrDefault(x => x.Type == type && FinancialVersions.SameName(x.Name, name));
            if (category is null) errors.Add("Создайте категорию с таким названием и типом.");
            else if (!category.IsActive) errors.Add("Восстановите категорию перед импортом.");
            var description = Cell(input.Mapping.Description);
            if (description.Length > 500) errors.Add("Описание: не больше 500 символов.");
            preview.Add(new(i + 1, validDate ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : dateText,
                validAmount ? FinancialVersions.Money(amount) : Cell(input.Mapping.Amount), name, type.ToString(), description, errors.ToArray()));
            if (errors.Count == 0) entries.Add(FinancialRules.Create(Owner, account, category!, type, amount, description, date, now));
        }
        return new(hash, new(preview, !duplicate && preview.All(x => x.Errors.Length == 0), duplicate, account.Currency), entries);
    }
}
