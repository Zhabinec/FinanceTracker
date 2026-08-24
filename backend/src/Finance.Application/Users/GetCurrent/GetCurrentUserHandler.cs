using Finance.Application.Abstractions;

namespace Finance.Application.Users.GetCurrent;

public sealed class GetCurrentUserHandler(
    ICurrentUser currentUser,
    IUserProfileStore userStore)
{
    public async Task<CurrentUserDto?> HandleAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return null;
        }

        var user = await userStore.FindByIdAsync(userId, cancellationToken);
        return user is null
            ? null
            : new CurrentUserDto(
                user.Id,
                user.Email,
                user.BaseCurrency,
                user.RegisteredAtUtc);
    }
}
