using Finance.Domain.Users;

namespace Finance.Application.Abstractions;

public interface IUserProfileStore
{
    Task<User?> FindByIdAsync(Guid userId, CancellationToken cancellationToken);
}
