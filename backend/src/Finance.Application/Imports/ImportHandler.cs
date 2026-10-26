using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Finance.Application.Abstractions;
using Finance.Application.Accounts;
using Finance.Application.Common;
using Finance.Application.Transactions;
using Finance.Domain.Common;
using Finance.Domain.Transactions;

namespace Finance.Application.Imports;

public sealed record CsvMapping(int Date, int Amount, int Category, int? Description, int? Type);
public sealed record ImportCommand(string Content, string Delimiter, Guid AccountId, CsvMapping Mapping, TransactionType DefaultType);
public sealed record ImportRow(int Row, string? Date, string? Amount, string Category, string? Type, string? Description, string[] Errors);
public sealed record ImportPreview(IReadOnlyList<ImportRow> Rows, bool CanApply, bool AlreadyImported, string Currency);
public sealed record ImportResult(Guid Id, int Count);
public interface IImportStore
{
    Task<bool> ExistsAsync(Guid userId, string fingerprint, CancellationToken cancellationToken);
    Task ApplyAsync(ImportBatch batch, IReadOnlyList<Finance.Domain.Transactions.Transaction> transactions, CancellationToken cancellationToken);
}

public sealed class ImportHandler(ICurrentUser currentUser, ICategoryStore categoryStore, IAccountStore accountStore,
    IImportStore store, IClock clock)
{
    public async Task<ImportPreview> PreviewAsync(ImportCommand command, CancellationToken cancellationToken) =>
        (await PrepareAsync(command, cancellationToken)).Preview;

    public async Task<ImportResult> ApplyAsync(ImportCommand command, CancellationToken cancellationToken)
    {
        var prepared = await PrepareAsync(command, cancellationToken);
        if (prepared.Preview.AlreadyImported) throw new ConflictException("Этот CSV уже импортирован. Повторное применение заблокировано.");
        if (!prepared.Preview.CanApply) throw new DomainException("Исправьте ошибки в предпросмотре. Ни одна операция не импортирована.");
        var batch = ImportBatch.Create(prepared.UserId, prepared.Fingerprint, prepared.Transactions.Count, clock.UtcNow);
        await store.ApplyAsync(batch, prepared.Transactions, cancellationToken);
        return new(batch.Id, batch.Count);
    }

    private sealed record Prepared(Guid UserId, string Fingerprint, ImportPreview Preview, IReadOnlyList<Finance.Domain.Transactions.Transaction> Transactions);
    private async Task<Prepared> PrepareAsync(ImportCommand command, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        if (command.Content is null || command.Delimiter is not ("," or ";" or "\t") || command.Mapping is null || !Enum.IsDefined(command.DefaultType))
            throw new DomainException("Проверьте файл, разделитель и тип операций.");
        var rows = CsvReader.Read(command.Content, command.Delimiter[0]);
        var columns = new int?[] { command.Mapping.Date, command.Mapping.Amount, command.Mapping.Category, command.Mapping.Description, command.Mapping.Type };
        if (columns.Any(i => i.HasValue && (i < 0 || i >= rows[0].Length)) || columns.Where(i => i.HasValue).Distinct().Count() != columns.Count(i => i.HasValue))
            throw new DomainException("Для каждого поля выберите отдельную существующую колонку.");
        var account = await accountStore.FindOwnedAsync(command.AccountId, userId, cancellationToken);
        if (account is null || account.IsArchived) throw new DomainException("Выберите свой активный счёт.");
        var categories = await categoryStore.ListByUserAsync(userId, true, cancellationToken);
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(command.Content.TrimStart('\uFEFF').Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n'))));
        var duplicate = await store.ExistsAsync(userId, fingerprint, cancellationToken);
        var preview = new List<ImportRow>();
        var transactions = new List<Finance.Domain.Transactions.Transaction>();
        var now = clock.UtcNow;
        for (var i = 1; i < rows.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = rows[i];
            string Cell(int? index) => index.HasValue && index.Value < row.Length ? row[index.Value].Trim() : "";
            var errors = new List<string>();
            if (row.Length != rows[0].Length) errors.Add("Число колонок отличается от заголовка.");
            var dateText = Cell(command.Mapping.Date);
            var validDate = DateOnly.TryParseExact(dateText, ["yyyy-MM-dd", "dd.MM.yyyy", "dd/MM/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) && date.Year is >= 1900 and <= 9998;
            if (!validDate) errors.Add("Дата: ГГГГ-ММ-ДД, ДД.ММ.ГГГГ или ДД/ММ/ГГГГ.");
            var amountText = Cell(command.Mapping.Amount).Replace(" ", "").Replace("\u00a0", "").Replace("\u202f", "").Replace(',', '.');
            var validAmount = decimal.TryParse(amountText, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount) && amount > 0 && amount <= Finance.Domain.Transactions.Transaction.MaxAmount && decimal.Round(amount, 2) == amount;
            if (!validAmount) errors.Add("Сумма должна быть положительной, не больше двух знаков после запятой. Тип задаётся отдельно.");
            var type = command.DefaultType;
            if (command.Mapping.Type.HasValue)
            {
                var value = Cell(command.Mapping.Type).ToLowerInvariant();
                if (value is "expense" or "расход") type = TransactionType.Expense;
                else if (value is "income" or "доход") type = TransactionType.Income;
                else errors.Add("Тип: Income/Доход или Expense/Расход.");
            }
            var name = Cell(command.Mapping.Category);
            var category = categories.SingleOrDefault(c => c.NormalizedName == name.ToUpperInvariant() && c.Type == type);
            if (category is null) errors.Add("Создайте категорию с таким названием и типом или исправьте CSV.");
            else if (!category.IsActive) errors.Add("Категория неактивна. Восстановите её перед импортом.");
            var description = Cell(command.Mapping.Description);
            if (description.Length > 500) errors.Add("Описание: не больше 500 символов.");
            preview.Add(new(i + 1, validDate ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : dateText,
                validAmount ? amount.ToString("F2", CultureInfo.InvariantCulture) : Cell(command.Mapping.Amount), name, type.ToString(), description, errors.ToArray()));
            if (errors.Count > 0) continue;
            var transaction = Finance.Domain.Transactions.Transaction.Create(userId, category!, type, amount, description, MoscowDate.ToUtc(date), now);
            transaction.AssignAccount(account);
            transactions.Add(transaction);
        }
        return new(userId, fingerprint, new(preview, !duplicate && preview.All(r => r.Errors.Length == 0), duplicate, account.Currency), transactions);
    }
}
