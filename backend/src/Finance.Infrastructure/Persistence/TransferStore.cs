using Finance.Application.Common;
using Finance.Application.Transfers;
using Finance.Domain.Accounts;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Finance.Infrastructure.Persistence;

public sealed class TransferStore(FinanceDbContext db) : ITransferStore
{
    public Task<AccountTransfer?> FindByRequestAsync(Guid userId, Guid requestId, CancellationToken cancellationToken) =>
        db.AccountTransfers.SingleOrDefaultAsync(t => t.UserId == userId && t.RequestId == requestId, cancellationToken);
    public Task<AccountTransfer?> FindOwnedAsync(Guid userId, Guid id, CancellationToken cancellationToken) =>
        db.AccountTransfers.SingleOrDefaultAsync(t => t.UserId == userId && t.Id == id, cancellationToken);

    public async Task<AccountTransfer> AddAsync(AccountTransfer transfer, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // Ordered share locks serialize assignment against archiving either account.
        var sourceId = transfer.SourceAccountId;
        var targetId = transfer.TargetAccountId;
        var accounts = await db.Accounts.FromSqlInterpolated($"SELECT * FROM accounts WHERE \"Id\" IN ({sourceId}, {targetId}) ORDER BY \"Id\" FOR SHARE")
            .AsNoTracking().ToListAsync(cancellationToken);
        if (accounts.Count != 2 || accounts.Any(a => a.UserId != transfer.UserId || a.IsArchived || a.Currency != transfer.Currency))
            throw new ConflictException("Один из счетов больше не доступен для перевода. Обновите список.");
        db.AccountTransfers.Add(transfer);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return transfer;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_account_transfers_UserId_RequestId" })
        {
            await transaction.RollbackAsync(cancellationToken);
            await transaction.DisposeAsync();
            db.Entry(transfer).State = EntityState.Detached;
            return await db.AccountTransfers.AsNoTracking().SingleAsync(t => t.UserId == transfer.UserId && t.RequestId == transfer.RequestId, cancellationToken);
        }
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { throw new ConflictException("Перевод уже изменён. Обновите историю."); }
    }

    public async Task<TransferPage> ListAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = db.AccountTransfers.AsNoTracking().Where(t => t.UserId == userId);
        var count = await query.CountAsync(cancellationToken);
        var rows = await (from t in query.OrderByDescending(t => t.CreatedAtUtc).ThenBy(t => t.Id).Skip((page - 1) * pageSize).Take(pageSize)
            join source in db.Accounts on new { Id = t.SourceAccountId, t.UserId } equals new { source.Id, source.UserId }
            join target in db.Accounts on new { Id = t.TargetAccountId, t.UserId } equals new { target.Id, target.UserId }
            select new { Transfer = t, Source = source.Name, Target = target.Name }).ToListAsync(cancellationToken);
        return new(rows.Select(t => TransferDto.From(t.Transfer, t.Source, t.Target)).ToArray(), count, page, pageSize);
    }
}
