using Finance.Domain.Common;

namespace Finance.Domain.Ledger;

public sealed class FinancialWorkspace
{
    private FinancialWorkspace() { }
    public Guid UserId { get; private set; }
    public Guid VaultId { get; private set; }
    public long SourceRevision { get; private set; }
    public string BaseCurrency { get; private set; } = "";
    public DateTimeOffset MigratedAtUtc { get; private set; }
    public static FinancialWorkspace Create(Guid userId, Guid vaultId, long revision, string currency, DateTimeOffset now)
    {
        if (userId == Guid.Empty || vaultId == Guid.Empty || revision < 1 || currency.Length != 3 ||
            currency.Any(c => c is < 'A' or > 'Z') || now.Offset != TimeSpan.Zero)
            throw new DomainException("Некорректное финансовое пространство.");
        return new() { UserId = userId, VaultId = vaultId, SourceRevision = revision, BaseCurrency = currency, MigratedAtUtc = now };
    }
}
