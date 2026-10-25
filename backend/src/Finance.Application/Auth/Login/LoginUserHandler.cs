using Finance.Application.Abstractions;
using Finance.Domain.Users;

namespace Finance.Application.Auth.Login;

public sealed class LoginUserHandler(
    IUserLoginStore userStore,
    IPasswordHasher passwordHasher,
    IAccessTokenProvider accessTokenProvider,
    IRefreshSessionStore refreshSessionStore,
    IRefreshTokenProtector refreshTokenProtector,
    IClock clock,
    Refresh.RefreshTokenSettings refreshTokenSettings)
{
    public async Task<LoginUserResult> HandleAsync(
        LoginUserCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Email) ||
            string.IsNullOrEmpty(command.Password) || command.Password.Length > 128)
        {
            return LoginUserResult.InvalidCredentials();
        }

        var normalizedEmail = User.NormalizeEmail(command.Email);
        var user = await userStore.FindByNormalizedEmailAsync(
            normalizedEmail,
            cancellationToken);

        if (user is null)
        {
            _ = passwordHasher.Hash(command.Password);
            return LoginUserResult.InvalidCredentials();
        }

        if (!passwordHasher.Verify(user.PasswordHash, command.Password))
        {
            return LoginUserResult.InvalidCredentials();
        }

        if (user.EmailConfirmedAtUtc is null) return LoginUserResult.EmailNotConfirmed();
        var accessToken = accessTokenProvider.Create(user);
        var refreshToken = refreshTokenProtector.Generate();
        var nowUtc = clock.UtcNow;
        var refreshExpiresAtUtc = nowUtc.AddDays(refreshTokenSettings.LifetimeDays);
        var session = RefreshSession.Create(user.Id,
            refreshTokenProtector.Hash(refreshToken), nowUtc, refreshExpiresAtUtc, user.SecurityStamp);
        await refreshSessionStore.AddAsync(session, cancellationToken);

        return LoginUserResult.Success(accessToken.Value, accessToken.ExpiresAtUtc,
            refreshToken, refreshExpiresAtUtc);
    }
}
