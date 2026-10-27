using Finance.Application.Auth.Google;
using Finance.Application.Common;
using Finance.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Finance.Infrastructure.Persistence;
public sealed class GoogleAuthStore(FinanceDbContext db) : IGoogleAuthStore
{
    public async Task<User> OpenAsync(GoogleProof proof, GoogleLinkOwner? link, string passwordHash, string tokenHash,
        DateTimeOffset now, DateTimeOffset expires, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Serialize registrations for the same external subject, including requests from separate instances.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({proof.Subject}, 42))", ct);
        var identity = await db.GoogleAccountLinks.AsNoTracking().SingleOrDefaultAsync(x => x.Subject == proof.Subject, ct);
        User user;
        if (link is not null || identity is not null)
        {
            var userId = link?.UserId ?? identity!.UserId;
            user = await db.Users.FromSqlInterpolated($"SELECT * FROM users WHERE \"Id\" = {userId} FOR UPDATE")
                .SingleOrDefaultAsync(ct) ?? throw new UnauthorizedAccessException();
            if (user.EmailConfirmedAtUtc is null || (link is not null && user.SecurityStamp != link.SecurityStamp))
                throw new UnauthorizedAccessException();
            if (identity is not null && identity.UserId != user.Id)
                throw new ConflictException("Google уже подключён к другому аккаунту.");
            if (identity is null)
            {
                if (await db.GoogleAccountLinks.AnyAsync(x => x.UserId == user.Id, ct))
                    throw new ConflictException("К аккаунту уже подключён другой Google.");
                db.GoogleAccountLinks.Add(GoogleAccountLink.Create(user.Id, proof.Subject));
            }
        }
        else
        {
            var normalized = User.NormalizeEmail(proof.Email);
            if (await db.Users.AnyAsync(x => x.NormalizedEmail == normalized, ct))
                throw new ConflictException("Сначала войдите в существующий аккаунт и подключите Google в настройках.");
            user = User.Create(proof.Email, passwordHash, now, "RUB");
            user.ConfirmEmail(now);
            db.Users.Add(user);
            db.Accounts.Add(Finance.Domain.Accounts.Account.CreatePrimary(user.Id, user.BaseCurrency));
            db.Categories.AddRange(Finance.Domain.Categories.StarterCategories.Create(user.Id));
            db.GoogleAccountLinks.Add(GoogleAccountLink.Create(user.Id, proof.Subject));
        }
        db.RefreshSessions.Add(RefreshSession.Create(user.Id, tokenHash, now, expires, user.SecurityStamp));
        try { await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { throw new ConflictException("Аккаунт уже существует. Войдите в него перед подключением Google."); }
        return user;
    }
}
