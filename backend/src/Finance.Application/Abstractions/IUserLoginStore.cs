using Finance.Domain.Users;

namespace Finance.Application.Abstractions;

public interface IUserLoginStore
{
    Task<User?> FindByNormalizedEmailAsync(
        string normalizedEmail,
        CancellationToken cancellationToken);
}
