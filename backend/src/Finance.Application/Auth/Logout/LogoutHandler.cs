using Finance.Application.Abstractions;

namespace Finance.Application.Auth.Logout;

public sealed class LogoutHandler(IRefreshSessionStore sessionStore,
    IRefreshTokenProtector tokenProtector, IClock clock)
{
    public async Task HandleAsync(LogoutCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.RefreshToken)) return;
        await sessionStore.RevokeAsync(tokenProtector.Hash(command.RefreshToken),
            clock.UtcNow, cancellationToken);
    }
}
