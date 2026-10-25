using Finance.Application.Auth.Recovery;
using Finance.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Finance.Infrastructure.Persistence;

public sealed class PasswordRecoveryStore(FinanceDbContext db) : IPasswordRecoveryStore
{
    public async Task<User?> IssueAsync(string normalizedEmail, string tokenHash, DateTimeOffset expiresAtUtc, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(item => item.NormalizedEmail == normalizedEmail, cancellationToken);
        if (user is null) return null;
        db.PasswordResetTokens.Add(PasswordResetToken.Create(user, tokenHash, expiresAtUtc));
        await db.SaveChangesAsync(cancellationToken);
        return user;
    }

    public async Task<bool> ResetAsync(string tokenHash, string passwordHash, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var reset = await db.PasswordResetTokens
            .FromSqlInterpolated($"SELECT * FROM password_reset_tokens WHERE \"TokenHash\" = {tokenHash} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (reset is null || reset.ConsumedAtUtc is not null || reset.ExpiresAtUtc <= nowUtc) return false;
        var user = await db.Users.FromSqlInterpolated($"SELECT * FROM users WHERE \"Id\" = {reset.UserId} FOR UPDATE")
            .SingleAsync(cancellationToken);
        if (!reset.CanConsume(user, nowUtc)) return false;
        reset.Consume(nowUtc);
        user.ChangePassword(passwordHash);
        await db.RefreshSessions.Where(item => item.UserId == user.Id && item.RevokedAtUtc == null)
            .ExecuteUpdateAsync(update => update.SetProperty(item => item.RevokedAtUtc, nowUtc), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}
