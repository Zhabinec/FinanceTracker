using Finance.Application.Auth.Verification;
using Finance.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Finance.Infrastructure.Persistence;

public sealed class EmailVerificationStore(FinanceDbContext db) : IEmailVerificationStore
{
    public async Task<User?> IssueAsync(string normalizedEmail, string tokenHash, DateTimeOffset expiresAtUtc, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(item => item.NormalizedEmail == normalizedEmail, cancellationToken);
        if (user is null || user.EmailConfirmedAtUtc.HasValue) return null;
        db.EmailConfirmationTokens.Add(EmailConfirmationToken.Create(user.Id, tokenHash, expiresAtUtc));
        await db.SaveChangesAsync(cancellationToken);
        return user;
    }

    public async Task<bool> ConfirmAsync(string tokenHash, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var token = await db.EmailConfirmationTokens
            .FromSqlInterpolated($"SELECT * FROM email_confirmation_tokens WHERE \"TokenHash\" = {tokenHash} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (token is null || !token.IsUsable(nowUtc)) return false;
        var user = await db.Users.FromSqlInterpolated($"SELECT * FROM users WHERE \"Id\" = {token.UserId} FOR UPDATE")
            .SingleAsync(cancellationToken);
        token.Consume(nowUtc);
        user.ConfirmEmail(nowUtc);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}
