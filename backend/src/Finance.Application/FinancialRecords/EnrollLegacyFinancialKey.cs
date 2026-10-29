using Finance.Application.Abstractions;
using Finance.Application.Messaging;

namespace Finance.Application.FinancialRecords;

public sealed record EnrollLegacyFinancialKey(Guid VaultId, long ExpectedRevision, string Code, int ConsentVersion) : IRequest<bool>, IAuthenticatedRequest;
public interface ILegacyKeyEnrollment { Task EnrollAsync(Guid owner, EnrollLegacyFinancialKey input, CancellationToken ct); }
public sealed class EnrollLegacyFinancialKeyHandler(ICurrentUser user, ILegacyKeyEnrollment enrollment) : IRequestHandler<EnrollLegacyFinancialKey, bool>
{
    public async Task<bool> HandleAsync(EnrollLegacyFinancialKey request, CancellationToken ct)
    { await enrollment.EnrollAsync(user.UserId ?? throw new UnauthorizedAccessException(), request, ct); return true; }
}
