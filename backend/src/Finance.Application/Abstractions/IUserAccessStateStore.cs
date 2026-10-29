namespace Finance.Application.Abstractions;

public sealed record UserAccessState(bool HasEmail, bool EmailConfirmed, Guid SecurityStamp, Guid? TelegramLinkVersion);
public interface IUserAccessStateStore
{
    Task<UserAccessState?> ReadAsync(Guid userId, CancellationToken cancellationToken);
}
