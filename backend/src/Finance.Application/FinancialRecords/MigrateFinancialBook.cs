using Finance.Application.Abstractions;
using Finance.Application.Messaging;

namespace Finance.Application.FinancialRecords;

public sealed record FinancialBookStatus(bool Ready, long? SourceRevision, string? BaseCurrency);
public sealed record MigrateFinancialBook(long ExpectedRevision) : IRequest<FinancialBookStatus>, IAuthenticatedRequest;
public interface IFinancialBookMigration
{
    Task<FinancialBookStatus> StatusAsync(Guid owner, CancellationToken ct);
    Task<FinancialBookStatus> MigrateAsync(Guid owner, long expectedRevision, CancellationToken ct);
}
public sealed class MigrateFinancialBookHandler(ICurrentUser current, IFinancialBookMigration migration)
    : IRequestHandler<MigrateFinancialBook, FinancialBookStatus>
{
    public Task<FinancialBookStatus> HandleAsync(MigrateFinancialBook request, CancellationToken ct) =>
        migration.MigrateAsync(current.UserId ?? throw new UnauthorizedAccessException(), request.ExpectedRevision, ct);
}
