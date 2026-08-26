using Finance.Application.Abstractions;
using Finance.Domain.Categories;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Finance.Infrastructure.Persistence;

public sealed class CategoryStore(FinanceDbContext dbContext) : ICategoryStore
{
    private const string UniqueNameConstraint = "IX_categories_UserId_Type_Name";

    public async Task<IReadOnlyList<Category>> ListByUserAsync(
        Guid userId,
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Categories
            .AsNoTracking()
            .Where(category => category.UserId == userId);

        if (!includeInactive)
        {
            query = query.Where(category => category.IsActive);
        }

        return await query
            .OrderBy(category => category.Type)
            .ThenBy(category => category.Name)
            .ToListAsync(cancellationToken);
    }

    public Task<Category?> FindOwnedAsync(
        Guid categoryId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        return dbContext.Categories.SingleOrDefaultAsync(
            category => category.Id == categoryId && category.UserId == userId,
            cancellationToken);
    }

    public async Task<bool> TryAddAsync(
        Category category,
        CancellationToken cancellationToken)
    {
        dbContext.Categories.Add(category);
        var saved = await TrySaveAsync(cancellationToken);

        if (!saved)
        {
            dbContext.Entry(category).State = EntityState.Detached;
        }

        return saved;
    }

    public async Task<bool> TrySaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: UniqueNameConstraint
            })
        {
            return false;
        }
    }
}
