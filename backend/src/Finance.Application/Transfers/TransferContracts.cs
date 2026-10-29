using System.Globalization;
using Finance.Application.Abstractions;
using Finance.Application.Accounts;
using Finance.Application.Common;
using Finance.Application.Transactions;
using Finance.Domain.Accounts;
using Finance.Domain.Common;

namespace Finance.Application.Transfers;

public sealed record CreateTransferCommand(Guid RequestId, Guid SourceAccountId, Guid TargetAccountId, decimal Amount, string? Description);
public sealed record TransferDto(Guid Id, Guid SourceAccountId, string SourceAccountName, Guid TargetAccountId, string TargetAccountName,
    string Currency, string Amount, string? Description, DateOnly OccurredOn, DateTimeOffset CreatedAtUtc, DateTimeOffset? CancelledAtUtc, Guid Version)
{
    public static TransferDto From(AccountTransfer transfer, string sourceName, string targetName) => new(
        transfer.Id, transfer.SourceAccountId, sourceName, transfer.TargetAccountId, targetName,
        transfer.Currency, transfer.Amount.ToString("F2", CultureInfo.InvariantCulture), transfer.Description,
        MoscowDate.FromUtc(transfer.CreatedAtUtc), transfer.CreatedAtUtc, transfer.CancelledAtUtc, transfer.Version);
}
public sealed record TransferPage(IReadOnlyList<TransferDto> Items, int TotalCount, int Page, int PageSize);
public sealed record CreatedTransfer(TransferDto Transfer, bool Created);
