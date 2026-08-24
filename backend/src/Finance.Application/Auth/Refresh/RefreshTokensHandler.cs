using Finance.Application.Abstractions;

namespace Finance.Application.Auth.Refresh;

public sealed class RefreshTokensHandler(IRefreshSessionStore sessionStore,
    IRefreshTokenProtector tokenProtector, IAccessTokenProvider accessTokenProvider,
    IClock clock, RefreshTokenSettings settings)
{
    public async Task<RefreshTokensResult> HandleAsync(RefreshTokensCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.RefreshToken))
            return RefreshTokensResult.InvalidToken();

        var nowUtc = clock.UtcNow;
        var newToken = tokenProtector.Generate();
        var newExpiresAtUtc = nowUtc.AddDays(settings.LifetimeDays);
        var user = await sessionStore.RotateAsync(
            tokenProtector.Hash(command.RefreshToken), tokenProtector.Hash(newToken),
            nowUtc, newExpiresAtUtc, cancellationToken);

        if (user is null) return RefreshTokensResult.InvalidToken();

        var accessToken = accessTokenProvider.Create(user);
        return RefreshTokensResult.Success(accessToken.Value, accessToken.ExpiresAtUtc,
            newToken, newExpiresAtUtc);
    }
}
