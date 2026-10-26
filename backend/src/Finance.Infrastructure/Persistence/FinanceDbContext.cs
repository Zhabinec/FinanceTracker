using Microsoft.EntityFrameworkCore;
using Finance.Domain.Categories;
using Finance.Domain.Users;
using FinanceTransaction = Finance.Domain.Transactions.Transaction;

namespace Finance.Infrastructure.Persistence;

public sealed class FinanceDbContext(DbContextOptions<FinanceDbContext> options)
    : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<TelegramAccountLink> TelegramAccountLinks => Set<TelegramAccountLink>();
    public DbSet<Finance.Domain.Transactions.TransactionRequest> TransactionRequests => Set<Finance.Domain.Transactions.TransactionRequest>();
    public DbSet<Finance.Domain.Transactions.ImportBatch> ImportBatches => Set<Finance.Domain.Transactions.ImportBatch>();
    public DbSet<Finance.Domain.Budgets.MonthlyBudget> MonthlyBudgets => Set<Finance.Domain.Budgets.MonthlyBudget>();
    public DbSet<Finance.Domain.Accounts.Account> Accounts => Set<Finance.Domain.Accounts.Account>();
    public DbSet<Finance.Domain.Accounts.AccountTransfer> AccountTransfers => Set<Finance.Domain.Accounts.AccountTransfer>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<EmailConfirmationToken> EmailConfirmationTokens => Set<EmailConfirmationToken>();

    public DbSet<RefreshSession> RefreshSessions => Set<RefreshSession>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<FinanceTransaction> Transactions => Set<FinanceTransaction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FinanceDbContext).Assembly);
    }
}
