using Finance.Domain.Accounts;
using Finance.Domain.Categories;
using Finance.Domain.Common;
using Finance.Domain.Transactions;

namespace Finance.Domain.Ledger;

public sealed record AccountData(Guid Id, string Name, string Currency, decimal OpeningBalance, decimal Balance, bool IsArchived, Guid Version);
public sealed record CategoryData(Guid Id, string Name, TransactionType Type, bool IsActive, Guid Version);
public sealed record TransactionData(Guid Id, Guid AccountId, Guid CategoryId, TransactionType Type, decimal Amount,
    string? Description, DateOnly OccurredOn, DateTimeOffset CreatedAtUtc, Guid Version);
public sealed record RequestReceipt(Guid RequestId, Guid ResourceId, FinancialRecordKind ResourceKind, string Fingerprint);
public sealed record BudgetData(Guid Id, Guid CategoryId, DateOnly Month, decimal Limit, Guid Version);
public sealed record TransferData(Guid Id, Guid RequestId, Guid SourceAccountId, Guid TargetAccountId, string Currency,
    decimal Amount, string? Description, DateOnly OccurredOn, DateTimeOffset CreatedAtUtc, DateTimeOffset? CancelledAtUtc, Guid Version);
public sealed record DailyTotalData(Guid AccountId, Guid CategoryId, DateOnly Day, TransactionType Type, decimal Amount, int Count);

public static class FinancialRules
{
    public static TransactionData Create(Guid owner, AccountData account, CategoryData category, TransactionType type,
        decimal amount, string? description, DateOnly day, DateTimeOffset now, TransactionData? previous = null)
    {
        if (account.IsArchived && (previous is null || previous.AccountId != account.Id))
            throw new DomainException("Архивный счёт не принимает новые операции.");
        if (!category.IsActive && (previous is null || previous.CategoryId != category.Id))
            throw new DomainException("Неактивная категория не принимает новые операции.");
        if (day.Year is < 1900 or > 9998) throw new DomainException("Дата должна быть между 1900 и 9998 годом.");
        // Reuse the existing domain's amount, type, description and ownership rules.
        var validatedCategory = Category.Create(owner, category.Name, category.Type);
        _ = Transaction.Create(owner, validatedCategory, type, amount, description,
            new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)), now);
        return new(previous?.Id ?? Guid.NewGuid(), account.Id, category.Id, type, amount,
            string.IsNullOrWhiteSpace(description) ? null : description.Trim(), day,
            FinancialRecord.CanonicalTimestamp(previous?.CreatedAtUtc ?? now), Guid.NewGuid());
    }
    public static AccountData CreateAccount(Guid owner, string name, string currency, decimal opening)
    {
        var account = Account.Create(owner, name, currency, opening);
        return new(account.Id, account.Name, account.Currency, account.OpeningBalance, account.OpeningBalance, false, account.Version);
    }
    public static decimal Signed(TransactionData value) => value.Type == TransactionType.Income ? value.Amount : -value.Amount;
}
