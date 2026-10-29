using System.Text.Json;
using Finance.Domain.Ledger;
using Finance.Domain.Transactions;

namespace Finance.Infrastructure.Security;

// Indexed identifiers and versions belong to the envelope. Encrypt only the remaining fields,
// rather than duplicating every GUID and date inside each ciphertext.
internal static class FinancialRecordPayload
{
    private sealed record AccountSecrets(string Name, string Currency, decimal OpeningBalance, decimal Balance, bool IsArchived);
    private sealed record CategorySecrets(string Name, TransactionType Type, bool IsActive);
    private sealed record TransactionSecrets(TransactionType Type, decimal Amount, string? Description);
    private sealed record BudgetSecrets(decimal Limit);
    private sealed record DailySecrets(TransactionType Type, decimal Amount, int Count);
    private sealed record TransferSecrets(Guid RequestId, Guid TargetAccountId, string Currency, decimal Amount, string? Description, DateTimeOffset? CancelledAtUtc);

    public static byte[] Serialize<T>(T value, JsonSerializerOptions json)
    {
        object? secrets = value switch {
            AccountData a => new AccountSecrets(a.Name, a.Currency, a.OpeningBalance, a.Balance, a.IsArchived),
            CategoryData c => new CategorySecrets(c.Name, c.Type, c.IsActive),
            TransactionData t => new TransactionSecrets(t.Type, t.Amount, t.Description),
            BudgetData b => new BudgetSecrets(b.Limit),
            DailyTotalData d => new DailySecrets(d.Type, d.Amount, d.Count),
            TransferData t => new TransferSecrets(t.RequestId, t.TargetAccountId, t.Currency, t.Amount, t.Description, t.CancelledAtUtc),
            _ => value
        };
        return JsonSerializer.SerializeToUtf8Bytes(secrets, json);
    }
    public static T Deserialize<T>(FinancialRecord row, byte[] plain, JsonSerializerOptions json)
    {
        object value;
        if (typeof(T) == typeof(AccountData))
        {
            var secrets = Read<AccountSecrets>();
            value = new AccountData(row.Id, secrets.Name, secrets.Currency, secrets.OpeningBalance, secrets.Balance, secrets.IsArchived, row.Version);
        }
        else if (typeof(T) == typeof(CategoryData))
        {
            var secrets = Read<CategorySecrets>(); value = new CategoryData(row.Id, secrets.Name, secrets.Type, secrets.IsActive, row.Version);
        }
        else if (typeof(T) == typeof(TransactionData))
        {
            var secrets = Read<TransactionSecrets>();
            value = new TransactionData(row.Id, row.AccountId!.Value, row.CategoryId!.Value, secrets.Type, secrets.Amount, secrets.Description,
                row.OccurredOn!.Value, row.CreatedAtUtc, row.Version);
        }
        else if (typeof(T) == typeof(BudgetData))
        { var secrets = Read<BudgetSecrets>(); value = new BudgetData(row.Id, row.CategoryId!.Value, row.OccurredOn!.Value, secrets.Limit, row.Version); }
        else if (typeof(T) == typeof(DailyTotalData))
        { var secrets = Read<DailySecrets>(); value = new DailyTotalData(row.AccountId!.Value, row.CategoryId!.Value, row.OccurredOn!.Value, secrets.Type, secrets.Amount, secrets.Count); }
        else if (typeof(T) == typeof(TransferData))
        {
            var secrets = Read<TransferSecrets>();
            value = new TransferData(row.Id, secrets.RequestId, row.AccountId!.Value, secrets.TargetAccountId, secrets.Currency, secrets.Amount,
                secrets.Description, row.OccurredOn!.Value, row.CreatedAtUtc, secrets.CancelledAtUtc, row.Version);
        }
        else value = Read<T>()!;
        return (T)value;

        TValue Read<TValue>() => JsonSerializer.Deserialize<TValue>(plain, json) ?? throw new System.Security.Cryptography.CryptographicException("Empty financial payload.");
    }
}
