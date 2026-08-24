using Finance.Application.Abstractions;
using Finance.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Finance.Infrastructure.Persistence;

public sealed class UserProfileStore(FinanceDbContext dbContext) : IUserProfileStore
{
    public Task<User?> FindByIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        return dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(user => user.Id == userId, cancellationToken);
    }
}
