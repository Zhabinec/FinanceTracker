using System.Globalization;
using Finance.Application.Abstractions;
using Finance.Application.Accounts;
using Finance.Application.Common;
using Finance.Application.Transactions;
using Finance.Domain.Accounts;
using Finance.Domain.Common;

namespace Finance.Application.Transfers;

public interface ITransferStore
{
    Task<AccountTransfer?> FindByRequestAsync(Guid userId, Guid requestId, CancellationToken cancellationToken);
    Task<AccountTransfer?> FindOwnedAsync(Guid userId, Guid id, CancellationToken cancellationToken);
    Task<AccountTransfer> AddAsync(AccountTransfer transfer, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
    Task<TransferPage> ListAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken);
}

public sealed class TransferHandler(ICurrentUser currentUser, IAccountStore accounts, ITransferStore store, IClock clock)
{
    private Guid UserId => currentUser.UserId ?? throw new UnauthorizedAccessException();
    public async Task<CreatedTransfer?> CreateAsync(CreateTransferCommand command, CancellationToken cancellationToken)
    {
        var userId = UserId;
        if (command.RequestId == Guid.Empty) throw new DomainException("Нужен идентификатор запроса перевода.");
        if (command.SourceAccountId == Guid.Empty || command.TargetAccountId == Guid.Empty) throw new DomainException("Выберите два счёта.");
        var existing = await store.FindByRequestAsync(userId, command.RequestId, cancellationToken);
        if (existing is not null) EnsureSameRequest(existing, command);
        var source = await accounts.FindOwnedAsync(command.SourceAccountId, userId, cancellationToken);
        var target = await accounts.FindOwnedAsync(command.TargetAccountId, userId, cancellationToken);
        if (source is null || target is null) return null;
        if (existing is not null) return new(TransferDto.From(existing, source.Name, target.Name), false);
        if (source.IsArchived || target.IsArchived) throw new ConflictException("Для перевода нужны два активных счёта.");
        var transfer = AccountTransfer.Create(userId, command.RequestId, source, target, command.Amount, command.Description, clock.UtcNow);
        var saved = await store.AddAsync(transfer, cancellationToken);
        EnsureSameRequest(saved, command);
        return new(TransferDto.From(saved, source.Name, target.Name), saved.Id == transfer.Id);
    }

    public Task<TransferPage> ListAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        var userId = UserId;
        if (page is < 1 or > 1_000_000 || pageSize is < 1 or > 100)
            throw new RequestValidationException(new Dictionary<string, string[]> { ["page"] = ["Неверные параметры страницы."] });
        return store.ListAsync(userId, page, pageSize, cancellationToken);
    }

    public async Task<bool> CancelAsync(Guid id, Guid version, CancellationToken cancellationToken)
    {
        var transfer = await store.FindOwnedAsync(UserId, id, cancellationToken);
        if (transfer is null) return false;
        if (transfer.CancelledAtUtc.HasValue) return true;
        if (version == Guid.Empty || version != transfer.Version) throw new ConflictException("Перевод уже изменён. Обновите историю.");
        transfer.Cancel(clock.UtcNow);
        await store.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static void EnsureSameRequest(AccountTransfer transfer, CreateTransferCommand command)
    {
        if (transfer.SourceAccountId != command.SourceAccountId || transfer.TargetAccountId != command.TargetAccountId ||
            transfer.Amount != command.Amount || transfer.Description != AccountTransfer.NormalizeDescription(command.Description))
            throw new ConflictException("Этот идентификатор запроса уже использован для другого перевода.");
    }
}
