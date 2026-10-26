using Finance.Application.Abstractions;
using Finance.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Finance.Infrastructure.Persistence;

public sealed class UserRegistrationStore(FinanceDbContext dbContext) : IUserRegistrationStore
{
    public Task<bool> EmailExistsAsync(
        string normalizedEmail,
        CancellationToken cancellationToken)
    {
        return dbContext.Users.AnyAsync(
            user => user.NormalizedEmail == normalizedEmail,
            cancellationToken);
    }

    public async Task<bool> TryAddAsync(User user, CancellationToken cancellationToken)
    {
        dbContext.Users.Add(user);
        var account = Finance.Domain.Accounts.Account.CreatePrimary(user.Id, user.BaseCurrency);
        dbContext.Accounts.Add(account);
        var categories = Finance.Domain.Categories.StarterCategories.Create(user.Id);
        dbContext.Categories.AddRange(categories);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: "IX_users_NormalizedEmail"
            })
        {
            dbContext.Entry(user).State = EntityState.Detached;
            dbContext.Entry(account).State = EntityState.Detached;
            foreach (var category in categories) dbContext.Entry(category).State = EntityState.Detached;
            return false;
        }
    }
}
