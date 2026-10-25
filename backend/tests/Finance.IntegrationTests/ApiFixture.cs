using Finance.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Finance.IntegrationTests;

public sealed class ApiFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer database = new PostgreSqlBuilder("postgres:17-alpine").Build();
    public WebApplicationFactory<Program> App { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await database.StartAsync();
        App = new TestApi(database.GetConnectionString());
        using var scope = App.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<FinanceDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await App.DisposeAsync();
        await database.DisposeAsync();
    }

    public HttpClient Client(bool cookies = true) => App.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"), HandleCookies = cookies, AllowAutoRedirect = false
    });

    private sealed class TestApi(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Database", connectionString);
            builder.UseSetting("Jwt:SigningKey", "integration-test-signing-key-with-at-least-32-bytes");
        }
    }
}
