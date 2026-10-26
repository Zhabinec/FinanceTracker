using Finance.Application.Abstractions;
using Finance.Domain.Users;
using Microsoft.EntityFrameworkCore;

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
        var ownerId = await dbContext.RefreshSessions.AsNoTracking().Where(session => session.TokenHash == currentTokenHash && session.TelegramLinkVersion == null)
            .Select(session => (Guid?)session.UserId).SingleOrDefaultAsync(cancellationToken);
        if (ownerId is null) return null;
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        // Match password reset's lock order: user first, sessions second.
        // SHARE permits independent devices to rotate, but blocks security-stamp changes.
        var user = await dbContext.Users.FromSqlInterpolated($"SELECT * FROM users WHERE \"Id\" = {ownerId.Value} FOR SHARE")
            .AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (user is null || user.EmailConfirmedAtUtc is null) return null;
        var current = await dbContext.RefreshSessions
            .FromSqlInterpolated($"SELECT * FROM refresh_sessions WHERE \"TokenHash\" = {currentTokenHash} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (current is null || current.TelegramLinkVersion is not null || current.UserId != user.Id || current.RevokedAtUtc is not null || current.ExpiresAtUtc <= nowUtc || current.SecurityStamp != user.SecurityStamp)
            return null;
        current.Revoke(nowUtc);
        dbContext.RefreshSessions.Add(RefreshSession.Create(current.UserId, newTokenHash, nowUtc, newExpiresAtUtc, user.SecurityStamp));
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return user;
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
