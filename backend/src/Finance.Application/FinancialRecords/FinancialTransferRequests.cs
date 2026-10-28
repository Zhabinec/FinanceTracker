using Finance.Application.Abstractions;
using Finance.Application.Messaging;
using Finance.Application.Common;
using Finance.Application.Transactions;
using Finance.Application.Transfers;
using Finance.Domain.Accounts;
using Finance.Domain.Common;
using Finance.Domain.Ledger;

namespace Finance.Application.FinancialRecords;

public sealed record ListFinancialTransfers(int Page, int PageSize) : IRequest<TransferPage>, IAuthenticatedRequest;
public sealed record CreateFinancialTransfer(Guid RequestId, Guid SourceAccountId, Guid TargetAccountId, decimal Amount, string? Description)
    : IRequest<TransferDto>, IAuthenticatedRequest;
public sealed record CancelFinancialTransfer(Guid Id, Guid Version) : IRequest<bool>, IAuthenticatedRequest;

public sealed class FinancialTransferHandler(ICurrentUser current, IFinancialBooks books, IClock clock)
    : IRequestHandler<ListFinancialTransfers, TransferPage>, IRequestHandler<CreateFinancialTransfer, TransferDto>,
      IRequestHandler<CancelFinancialTransfer, bool>
{
    private Guid Owner => current.UserId ?? throw new UnauthorizedAccessException();
    private static TransferDto Dto(TransferData t, string source, string target) => new(t.Id, t.SourceAccountId, source, t.TargetAccountId, target,
        t.Currency, FinancialVersions.Money(t.Amount), t.Description, t.OccurredOn, t.CreatedAtUtc, t.CancelledAtUtc, t.Version);
    public async Task<TransferPage> HandleAsync(ListFinancialTransfers request, CancellationToken ct)
    {
        if (request.Page is < 1 or > 1_000_000 || request.PageSize is < 1 or > 100) throw new DomainException("Неверные параметры страницы.");
        await using var book = await books.OpenAsync(Owner, false, ct);
        var accounts = (await book.AccountsAsync(ct)).ToDictionary(x => x.Id);
        var (items, count) = await book.TransfersAsync(request.Page, request.PageSize, ct);
        var result = new TransferPage(items.Select(x => Dto(x, accounts[x.SourceAccountId].Name, accounts[x.TargetAccountId].Name)).ToArray(), count, request.Page, request.PageSize);
        await book.CommitAsync(ct); return result;
    }
    public async Task<TransferDto> HandleAsync(CreateFinancialTransfer request, CancellationToken ct)
    {
        if (request.RequestId == Guid.Empty) throw new DomainException("Нужен идентификатор запроса перевода.");
        await using var book = await books.OpenAsync(Owner, true, ct);
        var source = await book.AccountAsync(request.SourceAccountId, ct) ?? throw new FinancialRecordNotFoundException();
        var target = await book.AccountAsync(request.TargetAccountId, ct) ?? throw new FinancialRecordNotFoundException();
        if (await book.TransferRequestAsync(request.RequestId, ct) is { } previous)
        {
            if (previous.SourceAccountId != source.Id || previous.TargetAccountId != target.Id || previous.Amount != request.Amount ||
                previous.Description != AccountTransfer.NormalizeDescription(request.Description)) throw new ConflictException("Запрос уже использован для другого перевода.");
            await book.CommitAsync(ct); return Dto(previous, source.Name, target.Name);
        }
        var validated = AccountTransfer.Create(Owner, request.RequestId, AccountEntity(source), AccountEntity(target), request.Amount, request.Description, clock.UtcNow);
        var value = new TransferData(validated.Id, request.RequestId, source.Id, target.Id, validated.Currency, validated.Amount,
            validated.Description, MoscowDate.FromUtc(validated.CreatedAtUtc), FinancialRecord.CanonicalTimestamp(validated.CreatedAtUtc), null, validated.Version);
        await book.SaveTransferAsync(value, false, ct); await book.CommitAsync(ct); return Dto(value, source.Name, target.Name);
    }
    public async Task<bool> HandleAsync(CancelFinancialTransfer request, CancellationToken ct)
    {
        await using var book = await books.OpenAsync(Owner, true, ct);
        var previous = await book.TransferAsync(request.Id, ct) ?? throw new FinancialRecordNotFoundException();
        if (previous.CancelledAtUtc.HasValue) { await book.CommitAsync(ct); return true; }
        FinancialVersions.Match(previous.Version, request.Version);
        await book.SaveTransferAsync(previous with { CancelledAtUtc = clock.UtcNow, Version = Guid.NewGuid() }, true, ct);
        await book.CommitAsync(ct); return true;
    }
    private Account AccountEntity(AccountData value)
    {
        var account = Account.Create(Owner, value.Name, value.Currency, value.OpeningBalance, value.Id);
        account.Change(value.Name, value.IsArchived); return account;
    }
}
