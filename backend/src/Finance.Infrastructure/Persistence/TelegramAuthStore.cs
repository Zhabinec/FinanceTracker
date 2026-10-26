using Finance.Application.Auth.Telegram;
using Finance.Application.Common;
using Finance.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Finance.Infrastructure.Persistence;

public sealed class TelegramAuthStore(FinanceDbContext db) : ITelegramAuthStore
{
    public Task<TelegramAccountLink?> FindLinkAsync(Guid userId, CancellationToken ct) =>
        db.TelegramAccountLinks.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId, ct);

    public async Task<TelegramSessionOwner?> OpenAsync(long telegramUserId, string tokenHash, DateTimeOffset now, DateTimeOffset expires, CancellationToken ct)
    {
        var owner = await db.TelegramAccountLinks.AsNoTracking().Where(x => x.TelegramUserId == telegramUserId)
            .Select(x => (Guid?)x.UserId).SingleOrDefaultAsync(ct);
        if (owner is null) return null;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var user = await LockUser(owner.Value, false, ct);
        var link = await FindLinkAsync(owner.Value, ct);
        if (user?.EmailConfirmedAtUtc is null || link?.TelegramUserId != telegramUserId) return null;
        AddSession(user, link, tokenHash, now, expires);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(user, link.Version);
    }

    public async Task<TelegramSessionOwner> BindAndOpenAsync(Guid userId, Guid securityStamp, long telegramUserId, string tokenHash, DateTimeOffset now, DateTimeOffset expires, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var user = await LockUser(userId, true, ct);
        if (user?.EmailConfirmedAtUtc is null || user.SecurityStamp != securityStamp) throw new UnauthorizedAccessException();
        var link = await FindLinkAsync(userId, ct);
        if (link is not null && link.TelegramUserId != telegramUserId) throw LinkConflict();
        if (await db.TelegramAccountLinks.AnyAsync(x => x.TelegramUserId == telegramUserId && x.UserId != userId, ct)) throw LinkConflict();
        if (link is null) { link = TelegramAccountLink.Create(userId, telegramUserId, now); db.TelegramAccountLinks.Add(link); }
        AddSession(user, link, tokenHash, now, expires);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { throw LinkConflict(); }
        await transaction.CommitAsync(ct);
        return new(user, link.Version);
    }

    public async Task<TelegramSessionOwner?> RotateAsync(string tokenHash, string newTokenHash, DateTimeOffset now, DateTimeOffset expires, CancellationToken ct)
    {
        var owner = await db.RefreshSessions.AsNoTracking().Where(x => x.TokenHash == tokenHash && x.TelegramLinkVersion != null)
            .Select(x => (Guid?)x.UserId).SingleOrDefaultAsync(ct);
        if (owner is null) return null;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var user = await LockUser(owner.Value, false, ct);
        if (user?.EmailConfirmedAtUtc is null) return null;
        var current = await db.RefreshSessions.FromSqlInterpolated($"SELECT * FROM refresh_sessions WHERE \"TokenHash\" = {tokenHash} FOR UPDATE").SingleOrDefaultAsync(ct);
        var link = await FindLinkAsync(user.Id, ct);
        if (current is null || link is null || current.TelegramLinkVersion != link.Version || current.UserId != user.Id ||
            current.RevokedAtUtc is not null || current.ExpiresAtUtc <= now || current.SecurityStamp != user.SecurityStamp) return null;
        current.Revoke(now);
        AddSession(user, link, newTokenHash, now, expires);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(user, link.Version);
    }

    public async Task<bool> UnlinkAsync(Guid userId, Guid securityStamp, Guid version, DateTimeOffset now, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var user = await LockUser(userId, true, ct);
        if (user is null || user.SecurityStamp != securityStamp) throw new UnauthorizedAccessException();
        var link = await db.TelegramAccountLinks.SingleOrDefaultAsync(x => x.UserId == userId, ct);
        if (link is null) return false;
        if (link.Version != version) throw new ConflictException("Привязка изменилась. Обновите страницу.");
        db.TelegramAccountLinks.Remove(link);
        await db.RefreshSessions.Where(x => x.UserId == userId && x.TelegramLinkVersion != null && x.RevokedAtUtc == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.RevokedAtUtc, now), ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    public Task RevokeAsync(string tokenHash, DateTimeOffset now, CancellationToken ct) =>
        db.RefreshSessions.Where(x => x.TokenHash == tokenHash && x.TelegramLinkVersion != null && x.RevokedAtUtc == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.RevokedAtUtc, now), ct);

    // Consistent with password reset and refresh: user lock first, session lock second.
    private Task<User?> LockUser(Guid userId, bool exclusive, CancellationToken ct) => exclusive
        ? db.Users.FromSqlInterpolated($"SELECT * FROM users WHERE \"Id\" = {userId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct)
        : db.Users.FromSqlInterpolated($"SELECT * FROM users WHERE \"Id\" = {userId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
    private void AddSession(User user, TelegramAccountLink link, string hash, DateTimeOffset now, DateTimeOffset expires) =>
        db.RefreshSessions.Add(RefreshSession.Create(user.Id, hash, now, expires, user.SecurityStamp, link.Version));
    private static ConflictException LinkConflict() => new("Telegram или этот аккаунт уже связан с другим аккаунтом. Сначала отключите прежнюю привязку в профиле.");
}
