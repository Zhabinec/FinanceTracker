namespace Finance.Application.Auth.Login;

public enum LoginUserStatus
{
    Success,
    InvalidCredentials
}

public sealed record LoginUserResult(
    LoginUserStatus Status,
    string? AccessToken,
    DateTimeOffset? ExpiresAtUtc)
{
    public static LoginUserResult Success(
        string accessToken,
        DateTimeOffset expiresAtUtc) =>
        new(LoginUserStatus.Success, accessToken, expiresAtUtc);

    public static LoginUserResult InvalidCredentials() =>
        new(LoginUserStatus.InvalidCredentials, null, null);
}
