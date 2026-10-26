using Finance.Application.Common;
using Finance.Application.Imports;
using Finance.Domain.Transactions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Finance.Infrastructure.Persistence;

public sealed class ImportStore(FinanceDbContext db) : IImportStore
{
    public Task<bool> ExistsAsync(Guid userId, string fingerprint, CancellationToken cancellationToken) =>
        db.ImportBatches.AnyAsync(b => b.UserId == userId && b.Fingerprint == fingerprint, cancellationToken);
    public async Task ApplyAsync(ImportBatch batch, IReadOnlyList<Finance.Domain.Transactions.Transaction> transactions, CancellationToken cancellationToken)
    {
        await using var write = await db.Database.BeginTransactionAsync(cancellationToken);
        // Lock in a stable order, then recheck states inside the same atomic write.
        foreach (var id in transactions.Select(t => t.CategoryId).Distinct().Order())
        {
            var category = await db.Categories.FromSqlInterpolated($"SELECT * FROM categories WHERE \"Id\" = {id} FOR SHARE")
                .AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            if (category is null || category.UserId != batch.UserId || !category.IsActive)
                throw new ConflictException("Категория изменилась. Повторите предпросмотр.");
        }
        foreach (var id in transactions.Select(t => t.AccountId).Distinct().Order())
        {
            var account = await db.Accounts.FromSqlInterpolated($"SELECT * FROM accounts WHERE \"Id\" = {id} FOR SHARE")
                .AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            if (account is null || account.UserId != batch.UserId || account.IsArchived)
                throw new ConflictException("Счёт изменился. Повторите предпросмотр.");
        }
        db.ImportBatches.Add(batch);
        db.Transactions.AddRange(transactions);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await write.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_import_batches_UserId_Fingerprint" })
        { throw new ConflictException("Этот CSV уже импортирован. Ни одна дополнительная операция не создана."); }
    }
}
