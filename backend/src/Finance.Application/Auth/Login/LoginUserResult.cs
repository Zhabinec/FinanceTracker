namespace Finance.Application.Auth.Login;

public enum LoginUserStatus
{
    Success,
    InvalidCredentials,
    EmailNotConfirmed
}

public sealed record LoginUserResult(
    LoginUserStatus Status,
    string? AccessToken,
    DateTimeOffset? ExpiresAtUtc,
    string? RefreshToken,
    DateTimeOffset? RefreshTokenExpiresAtUtc)
{
    public static LoginUserResult Success(
        string accessToken,
        DateTimeOffset expiresAtUtc,
        string refreshToken,
        DateTimeOffset refreshTokenExpiresAtUtc) =>
        new(LoginUserStatus.Success, accessToken, expiresAtUtc,
            refreshToken, refreshTokenExpiresAtUtc);

    public static LoginUserResult InvalidCredentials() =>
        new(LoginUserStatus.InvalidCredentials, null, null, null, null);
    public static LoginUserResult EmailNotConfirmed() =>
        new(LoginUserStatus.EmailNotConfirmed, null, null, null, null);
}
