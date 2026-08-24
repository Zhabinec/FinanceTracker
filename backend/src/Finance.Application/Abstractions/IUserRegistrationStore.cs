using Finance.Domain.Users;

namespace Finance.Application.Abstractions;

public interface IUserRegistrationStore
{
    Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken cancellationToken);

    Task<bool> TryAddAsync(User user, CancellationToken cancellationToken);
}
