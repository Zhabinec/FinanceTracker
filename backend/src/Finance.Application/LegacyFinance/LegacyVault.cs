using Finance.Application.Abstractions;
using Finance.Domain.Users;

namespace Finance.Application.LegacyFinance;

public sealed record VaultEnvelope(Guid Id, long Revision, Guid MutationId, string Nonce, string Ciphertext, bool IsActive, DateTimeOffset UpdatedAtUtc, int Format = 1)
{
    public static VaultEnvelope From(PrivateFinancialVault vault) => new(vault.Id, vault.Revision, vault.MutationId, vault.Nonce, vault.Ciphertext, vault.IsActive, vault.UpdatedAtUtc);
}
public sealed record LegacyFinancialExport(string Document, string Fingerprint);
