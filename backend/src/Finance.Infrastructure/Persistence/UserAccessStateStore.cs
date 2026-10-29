using Finance.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Finance.Infrastructure.Persistence;

// One small projection per authenticated request; no password hash or stale access cache.
public sealed class UserAccessStateStore(FinanceDbContext dbContext) : IUserAccessStateStore
{
    public Task<UserAccessState?> ReadAsync(Guid userId, CancellationToken cancellationToken) =>
        (from user in dbContext.Users.AsNoTracking()
         where user.Id == userId
         from link in dbContext.TelegramAccountLinks.Where(link => link.UserId == user.Id).DefaultIfEmpty()
         select new UserAccessState(user.Email != null, user.EmailConfirmedAtUtc != null, user.SecurityStamp,
             link == null ? null : (Guid?)link.Version)).SingleOrDefaultAsync(cancellationToken);
}
