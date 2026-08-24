using Finance.Application.Abstractions;
using Finance.Domain.Users;

namespace Finance.Application.Auth.Login;

public sealed class LoginUserHandler(
    IUserLoginStore userStore,
    IPasswordHasher passwordHasher,
    IAccessTokenProvider accessTokenProvider)
{
    public async Task<LoginUserResult> HandleAsync(
        LoginUserCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Email) ||
            string.IsNullOrEmpty(command.Password))
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

        var accessToken = accessTokenProvider.Create(user);
        return LoginUserResult.Success(accessToken.Value, accessToken.ExpiresAtUtc);
    }
}
