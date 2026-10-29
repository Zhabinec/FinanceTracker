using System.Collections.Concurrent;
using System.Data.Common;
using Finance.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Finance.IntegrationTests;

public sealed partial class FinanceApiTests
{
    private sealed class QueryCounter : DbCommandInterceptor
    {
        public ConcurrentQueue<string> Commands { get; } = new();
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Commands.Enqueue(command.CommandText); return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { Commands.Enqueue(command.CommandText); return ValueTask.FromResult(result); }
    }
    [Fact]
    public async Task ReturningOpenAndInitialReportsHaveBoundedSqlWithoutRedundantScans()
    {
        var counter = new QueryCounter();
        using var app = ManagedApp().WithWebHostBuilder(builder => {
            builder.UseSetting("FinanceRecords:Enabled", "true");
            builder.ConfigureTestServices(services => services.AddDbContext<FinanceDbContext>(options => options.AddInterceptors(counter)));
        });
        using var client = ManagedClient(app); await RegisterManaged(client);
        (await client.PostAsync("/api/finance/open", null)).EnsureSuccessStatusCode();
        counter.Commands.Clear();
        (await client.PostAsync("/api/finance/open", null)).EnsureSuccessStatusCode();
        Assert.Equal(2, counter.Commands.Count); // Current access state + existing workspace, no migration lock.
        Assert.DoesNotContain(counter.Commands, sql => sql.Contains("pg_advisory"));
        Assert.DoesNotContain(counter.Commands, sql => sql.Contains("PasswordHash"));
        counter.Commands.Clear();
        (await client.GetAsync("/api/finance/dashboard")).EnsureSuccessStatusCode();
        Assert.Equal(5, counter.Commands.Count); // Access + workspace/key + categories + daily totals + five recent rows.
        Assert.DoesNotContain(counter.Commands, sql => sql.Contains("count(", StringComparison.OrdinalIgnoreCase));
        counter.Commands.Clear();
        (await client.GetAsync("/api/finance/budgets?month=2026-10-01")).EnsureSuccessStatusCode();
        Assert.Equal(3, counter.Commands.Count); // No categories or daily totals when no budgets exist.
    }
}
