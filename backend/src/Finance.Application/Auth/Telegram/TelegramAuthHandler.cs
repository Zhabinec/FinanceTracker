using Finance.Application.Abstractions;
using Finance.Application.Auth.Refresh;
using Finance.Domain.Users;
using Finance.Application.Common;

namespace Finance.Application.Auth.Telegram;

public sealed class TelegramSettings(string botToken, string botUsername, int initDataLifetimeSeconds = 600)
{
    public string BotToken { get; } = botToken;
    public string BotUsername { get; } = botUsername;
    public bool Enabled => !string.IsNullOrWhiteSpace(BotToken);
    public int InitDataLifetimeSeconds { get; } = initDataLifetimeSeconds is >= 30 and <= 86400
        ? initDataLifetimeSeconds : throw new ArgumentOutOfRangeException(nameof(initDataLifetimeSeconds));
}
public sealed record TelegramProof(long UserId);
public interface ITelegramInitDataVerifier { TelegramProof? Verify(string? initData); }
public sealed record TelegramSessionOwner(User User, Guid LinkVersion);
public sealed record TelegramTokens(string AccessToken, DateTimeOffset ExpiresAtUtc, string RefreshToken);
public sealed record TelegramLinkStatus(bool Linked, Guid? Version);
public interface ITelegramAuthStore
{
    Task<TelegramSessionOwner?> OpenAsync(long telegramUserId, string tokenHash, DateTimeOffset now, DateTimeOffset expires, CancellationToken cancellationToken);
    Task<TelegramSessionOwner> BindAndOpenAsync(Guid userId, Guid securityStamp, long telegramUserId, string tokenHash, DateTimeOffset now, DateTimeOffset expires, CancellationToken cancellationToken);
    Task<TelegramSessionOwner?> RotateAsync(string tokenHash, string newTokenHash, DateTimeOffset now, DateTimeOffset expires, CancellationToken cancellationToken);
    Task<TelegramAccountLink?> FindLinkAsync(Guid userId, CancellationToken cancellationToken);
    Task<bool> UnlinkAsync(Guid userId, Guid securityStamp, Guid version, DateTimeOffset now, CancellationToken cancellationToken);
    Task RevokeAsync(string tokenHash, DateTimeOffset now, CancellationToken cancellationToken);
}
public interface ITelegramAccessTokenProvider { AccessToken CreateTelegram(User user, Guid linkVersion); }

public sealed class TelegramAuthHandler(ITelegramInitDataVerifier verifier, ITelegramAuthStore store,
    IUserLoginStore users, IPasswordHasher passwords, IRefreshTokenProtector tokens,
    ITelegramAccessTokenProvider accessTokens, IClock clock, RefreshTokenSettings refreshSettings)
{
    public async Task<TelegramTokens?> LoginAsync(string? initData, CancellationToken cancellationToken)
    {
        var proof = Verify(initData);
        var refresh = tokens.Generate();
        var now = clock.UtcNow;
        var owner = await store.OpenAsync(proof.UserId, tokens.Hash(refresh), now, now.AddDays(refreshSettings.LifetimeDays), cancellationToken);
        return owner is null ? null : Issue(owner, refresh);
    }

    public async Task<TelegramTokens> LinkAsync(string? initData, string? email, string? password, CancellationToken cancellationToken)
    {
        var proof = Verify(initData);
        if (string.IsNullOrWhiteSpace(email) || email.Length > 320 || password is null || password.Length is < 1 or > 128)
            throw new TelegramAuthenticationException("Неверный email или пароль.");
        var user = await users.FindByNormalizedEmailAsync(User.NormalizeEmail(email), cancellationToken);
        if (user is null) { _ = passwords.Hash(password); throw new TelegramAuthenticationException("Неверный email или пароль."); }
        if (!passwords.Verify(user.PasswordHash, password)) throw new TelegramAuthenticationException("Неверный email или пароль.");
        if (user.EmailConfirmedAtUtc is null) throw new Finance.Application.Common.ConflictException("Сначала подтвердите email аккаунта.");
        var refresh = tokens.Generate();
        var now = clock.UtcNow;
        var owner = await store.BindAndOpenAsync(user.Id, user.SecurityStamp, proof.UserId, tokens.Hash(refresh), now, now.AddDays(refreshSettings.LifetimeDays), cancellationToken);
        return Issue(owner, refresh);
    }

    public async Task<TelegramTokens> RefreshAsync(string? refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(refreshToken) || refreshToken.Length > 256) throw new UnauthorizedAccessException();
        var refresh = tokens.Generate();
        var now = clock.UtcNow;
        var owner = await store.RotateAsync(tokens.Hash(refreshToken), tokens.Hash(refresh), now, now.AddDays(refreshSettings.LifetimeDays), cancellationToken);
        return owner is null ? throw new UnauthorizedAccessException() : Issue(owner, refresh);
    }

    public Task LogoutAsync(string? refreshToken, CancellationToken cancellationToken) =>
        string.IsNullOrEmpty(refreshToken) || refreshToken.Length > 256 ? Task.CompletedTask : store.RevokeAsync(tokens.Hash(refreshToken), clock.UtcNow, cancellationToken);

    private TelegramProof Verify(string? initData) => verifier.Verify(initData)
        ?? throw new TelegramAuthenticationException("Не удалось подтвердить запуск. Закройте приложение и откройте его через Telegram.");
    private TelegramTokens Issue(TelegramSessionOwner owner, string refresh)
    {
        var access = accessTokens.CreateTelegram(owner.User, owner.LinkVersion);
        return new(access.Value, access.ExpiresAtUtc, refresh);
    }
}
