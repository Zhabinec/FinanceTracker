using System.Data;
using Finance.Application.Abstractions;
using Finance.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Finance.Infrastructure.Persistence;

public sealed class RefreshSessionStore(FinanceDbContext dbContext) : IRefreshSessionStore
{
    public async Task AddAsync(RefreshSession session, CancellationToken cancellationToken)
    {
        dbContext.RefreshSessions.Add(session);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<User?> RotateAsync(string currentTokenHash, string newTokenHash,
        DateTimeOffset nowUtc, DateTimeOffset newExpiresAtUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);

            var current = await dbContext.RefreshSessions
                .SingleOrDefaultAsync(session => session.TokenHash == currentTokenHash,
                    cancellationToken);

            if (current is null || current.RevokedAtUtc is not null || current.ExpiresAtUtc <= nowUtc)
                return null;

            var user = await dbContext.Users.AsNoTracking()
                .SingleAsync(item => item.Id == current.UserId, cancellationToken);
            if (current.SecurityStamp != user.SecurityStamp || user.EmailConfirmedAtUtc is null) return null;
            current.Revoke(nowUtc);
            dbContext.RefreshSessions.Add(RefreshSession.Create(
                current.UserId, newTokenHash, nowUtc, newExpiresAtUtc, user.SecurityStamp));
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return user;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.SerializationFailure)
        {
            dbContext.ChangeTracker.Clear();
            return null;
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
                  { SqlState: PostgresErrorCodes.SerializationFailure })
        {
            dbContext.ChangeTracker.Clear();
            return null;
        }
    }

    public async Task RevokeAsync(string tokenHash, DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var session = await dbContext.RefreshSessions
            .SingleOrDefaultAsync(item => item.TokenHash == tokenHash, cancellationToken);
        if (session is null || session.RevokedAtUtc is not null) return;

        session.Revoke(nowUtc);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
