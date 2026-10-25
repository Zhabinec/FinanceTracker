using Finance.Application.Accounts;
using Finance.Application.Common;
using Finance.Domain.Accounts;
using Finance.Domain.Transactions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Finance.Infrastructure.Persistence;

public sealed class AccountStore(FinanceDbContext db) : IAccountStore
{
    public async Task<IReadOnlyList<AccountBalance>> ListAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Accounts.AsNoTracking().Where(a => a.UserId == userId)
            .OrderBy(a => a.IsArchived).ThenBy(a => a.Name).ThenBy(a => a.Id)
            .Select(a => new AccountBalance(a, a.OpeningBalance +
                (db.Transactions.Where(t => t.UserId == userId && t.AccountId == a.Id)
                    .Sum(t => (decimal?)(t.Type == TransactionType.Income ? t.Amount : -t.Amount)) ?? 0m)))
            .ToListAsync(cancellationToken);

    public Task<Account?> FindOwnedAsync(Guid id, Guid userId, CancellationToken cancellationToken) =>
        db.Accounts.SingleOrDefaultAsync(a => a.Id == id && a.UserId == userId, cancellationToken);
    public void Add(Account account) => db.Accounts.Add(account);
    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { throw new ConflictException("Счёт уже изменён. Обновите список."); }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_accounts_UserId_NormalizedName" })
        { throw new ConflictException("Счёт с таким названием уже существует, в том числе в архиве."); }
    }
}
