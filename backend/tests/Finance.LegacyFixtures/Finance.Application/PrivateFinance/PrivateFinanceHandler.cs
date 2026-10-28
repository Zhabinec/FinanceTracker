using Finance.Application.Abstractions;
using Finance.Domain.Users;

namespace Finance.Application.LegacyFinance;

public sealed record StageVault(Guid Id, Guid MutationId, string Nonce, string Ciphertext, string LegacyFingerprint, int Format = 1);
public sealed record WriteVault(Guid Id, long ExpectedRevision, Guid MutationId, string Nonce, string Ciphertext, int Format = 1);
public interface IPrivateFinanceStore
{
    Task<VaultEnvelope?> ReadAsync(Guid userId, CancellationToken ct);
    Task<LegacyFinancialExport> ExportAsync(Guid userId, CancellationToken ct);
    Task<VaultEnvelope> StageAsync(Guid userId, StageVault input, CancellationToken ct);
    Task<VaultEnvelope> ActivateAsync(Guid userId, Guid id, long revision, CancellationToken ct);
    Task<VaultEnvelope> WriteAsync(Guid userId, WriteVault input, CancellationToken ct);
    Task CancelStageAsync(Guid userId, Guid id, long revision, CancellationToken ct);
}
public sealed class PrivateFinanceHandler(ICurrentUser currentUser, IPrivateFinanceStore store)
{
    private Guid UserId => currentUser.UserId ?? throw new UnauthorizedAccessException();
    public Task<VaultEnvelope?> ReadAsync(CancellationToken ct) => store.ReadAsync(UserId, ct);
    public Task<LegacyFinancialExport> ExportAsync(CancellationToken ct) => store.ExportAsync(UserId, ct);
    public Task<VaultEnvelope> StageAsync(StageVault input, CancellationToken ct) => store.StageAsync(UserId, input, ct);
    public Task<VaultEnvelope> ActivateAsync(Guid id, long revision, CancellationToken ct) => store.ActivateAsync(UserId, id, revision, ct);
    public Task<VaultEnvelope> WriteAsync(WriteVault input, CancellationToken ct) => store.WriteAsync(UserId, input, ct);
    public Task CancelStageAsync(Guid id, long revision, CancellationToken ct) => store.CancelStageAsync(UserId, id, revision, ct);
}
