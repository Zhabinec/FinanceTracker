using Finance.Application.Abstractions;
using Finance.Application.Messaging;

namespace Finance.Application.FinancialRecords;

public sealed record FinancialBookStatus(bool Ready, long? SourceRevision, string? BaseCurrency, long Revision = 0, bool RequiresKey = false);
public sealed record OpenFinancialBook : IRequest<FinancialBookStatus>, IAuthenticatedRequest;
public sealed record MigrateFinancialBook(long ExpectedRevision) : IRequest<FinancialBookStatus>, IAuthenticatedRequest;
public interface IFinancialBookMigration
{
    Task<FinancialBookStatus> StatusAsync(Guid owner, CancellationToken ct);
    Task<FinancialBookStatus> MigrateAsync(Guid owner, long expectedRevision, CancellationToken ct);
    Task<FinancialBookStatus> OpenAsync(Guid owner, CancellationToken ct);
}
public sealed class MigrateFinancialBookHandler(ICurrentUser current, IFinancialBookMigration migration)
    : IRequestHandler<MigrateFinancialBook, FinancialBookStatus>, IRequestHandler<OpenFinancialBook, FinancialBookStatus>
{
    public Task<FinancialBookStatus> HandleAsync(OpenFinancialBook request, CancellationToken ct) =>
        migration.OpenAsync(current.UserId ?? throw new UnauthorizedAccessException(), ct);
    public Task<FinancialBookStatus> HandleAsync(MigrateFinancialBook request, CancellationToken ct) =>
        migration.MigrateAsync(current.UserId ?? throw new UnauthorizedAccessException(), request.ExpectedRevision, ct);
}
