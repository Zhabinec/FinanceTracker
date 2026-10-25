using Microsoft.EntityFrameworkCore;
using Finance.Domain.Categories;
using Finance.Domain.Users;
using FinanceTransaction = Finance.Domain.Transactions.Transaction;

namespace Finance.Infrastructure.Persistence;

public sealed class FinanceDbContext(DbContextOptions<FinanceDbContext> options)
    : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();

    public DbSet<RefreshSession> RefreshSessions => Set<RefreshSession>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<FinanceTransaction> Transactions => Set<FinanceTransaction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FinanceDbContext).Assembly);
    }
}
