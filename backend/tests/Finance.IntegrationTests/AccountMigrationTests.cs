using Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Finance.IntegrationTests;

public sealed partial class FinanceApiTests
{
    [Fact]
    public async Task AccountsMigration_PreservesExistingUsersTransactionsAndTotals()
    {
        using var scope = fixture.App.Services.CreateScope();
        var connectionString = scope.ServiceProvider.GetRequiredService<FinanceDbContext>().Database.GetConnectionString();
        var databaseName = $"migration_{Guid.NewGuid():N}";
        await using var admin = new NpgsqlConnection(connectionString);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", admin))
            await create.ExecuteNonQueryAsync();
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString) { Database = databaseName, Pooling = false };
            await using var db = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>().UseNpgsql(builder.ConnectionString).Options);
            await db.GetService<IMigrator>().MigrateAsync("20261025171935_RequireConfirmedEmail");
            var owner = Guid.NewGuid();
            var category = Guid.NewGuid();
            var transaction = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO users ("Id", "Email", "NormalizedEmail", "PasswordHash", "RegisteredAtUtc", "BaseCurrency", "SecurityStamp")
                VALUES ({owner}, 'legacy@example.com', 'LEGACY@EXAMPLE.COM', 'test-hash', {now}, 'RUB', {Guid.NewGuid()});
                INSERT INTO categories ("Id", "UserId", "Name", "NormalizedName", "Type", "IsActive")
                VALUES ({category}, {owner}, 'Еда', 'ЕДА', { (int)Finance.Domain.Transactions.TransactionType.Expense }, true);
                INSERT INTO transactions ("Id", "UserId", "CategoryId", "Type", "Amount", "Description", "OccurredAtUtc", "CreatedAtUtc", "Version")
                VALUES ({transaction}, {owner}, {category}, { (int)Finance.Domain.Transactions.TransactionType.Expense }, 123.45, 'legacy', {now}, {now}, {Guid.NewGuid()});
                """);
            await db.Database.MigrateAsync();
            var account = Assert.Single(await db.Accounts.AsNoTracking().ToArrayAsync());
            Assert.Equal(owner, account.Id);
            Assert.Equal(owner, account.UserId);
            Assert.Equal("RUB", account.Currency);
            Assert.Equal(0, account.OpeningBalance);
            var preserved = Assert.Single(await db.Transactions.AsNoTracking().ToArrayAsync());
            Assert.Equal(transaction, preserved.Id);
            Assert.Equal(account.Id, preserved.AccountId);
            Assert.Equal(123.45m, preserved.Amount);
            Assert.Equal(-123.45m, Assert.Single(await new AccountStore(db).ListAsync(owner, CancellationToken.None)).Balance);
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE \"{databaseName}\" WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
