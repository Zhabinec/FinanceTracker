namespace Finance.Application.Auth.Refresh;

public enum RefreshTokensStatus { Success, InvalidToken }

public sealed record RefreshTokensResult(RefreshTokensStatus Status,
    string? AccessToken, DateTimeOffset? AccessTokenExpiresAtUtc,
    string? RefreshToken, DateTimeOffset? RefreshTokenExpiresAtUtc)
{
    public static RefreshTokensResult Success(string accessToken,
        DateTimeOffset accessTokenExpiresAtUtc, string refreshToken,
        DateTimeOffset refreshTokenExpiresAtUtc) =>
        new(RefreshTokensStatus.Success, accessToken, accessTokenExpiresAtUtc,
            refreshToken, refreshTokenExpiresAtUtc);

    public static RefreshTokensResult InvalidToken() => new(RefreshTokensStatus.InvalidToken, null, null, null, null);
}
