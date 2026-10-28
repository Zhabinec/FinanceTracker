using Finance.Application.Abstractions;

namespace Finance.Application.PrivateFinance;

public sealed record EnrollFinancialKey(Guid VaultId, long ExpectedRevision, string Code, int ConsentVersion);
public interface IManagedKeyStore
{
    Task<VaultEnvelope> StartAsync(Guid userId, CancellationToken ct);
    Task<string?> ReadAsync(Guid userId, CancellationToken ct);
    Task EnrollAsync(Guid userId, EnrollFinancialKey input, CancellationToken ct);
}
public sealed class ManagedKeyHandler(ICurrentUser user, IManagedKeyStore store)
{
    private Guid UserId => user.UserId ?? throw new UnauthorizedAccessException();
    public Task<VaultEnvelope> StartAsync(CancellationToken ct) => store.StartAsync(UserId, ct);
    public Task<string?> ReadAsync(CancellationToken ct) => store.ReadAsync(UserId, ct);
    public Task EnrollAsync(EnrollFinancialKey input, CancellationToken ct) => store.EnrollAsync(UserId, input, ct);
}
