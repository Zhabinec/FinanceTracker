namespace Finance.Application.LegacyFinance;

// Read-only compatibility source. The production application cannot create or edit old financial rows.
public interface ILegacyFinancialSource
{
    Task<VaultEnvelope?> ReadAsync(Guid owner, CancellationToken ct);
    Task<LegacyFinancialExport> ExportAsync(Guid owner, CancellationToken ct);
    Task DeleteRowsAsync(Guid owner, CancellationToken ct);
}
