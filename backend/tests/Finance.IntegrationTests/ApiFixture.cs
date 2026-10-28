using Finance.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Finance.Application.Auth.Recovery;
using Finance.Application.Auth.Verification;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Collections.Concurrent;

namespace Finance.IntegrationTests;

public sealed class ApiFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer database = new PostgreSqlBuilder("postgres:17-alpine").Build();
    public WebApplicationFactory<Program> App { get; private set; } = null!;
    public CaptureRecoveryEmailSender Emails { get; } = new();

    public async Task InitializeAsync()
    {
        await database.StartAsync();
        App = new TestApi(database.GetConnectionString(), Emails);
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

    private sealed class TestApi(string connectionString, CaptureRecoveryEmailSender emails) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Database", connectionString);
            builder.UseSetting("Jwt:SigningKey", "integration-test-signing-key-with-at-least-32-bytes");
            builder.UseSetting("Auth:RecoveryPermitLimit", "1000");
            builder.UseSetting("Auth:LoginPermitLimit", "1000");
            builder.UseSetting("Auth:RegistrationPermitLimit", "1000");
            builder.UseSetting("Http:ApiPermitLimit", "10000");
            builder.UseSetting("Telegram:BotToken", TelegramTestData.BotToken);
            builder.UseSetting("Telegram:BotUsername", "test_finance_bot");
            builder.UseSetting("Telegram:AuthPermitLimit", "1000");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IRecoveryEmailSender>();
                services.AddSingleton<IRecoveryEmailSender>(emails);
                services.RemoveAll<IConfirmationEmailSender>();
                services.AddSingleton<IConfirmationEmailSender>(emails);
            });
        }
    }
}

public sealed class CaptureRecoveryEmailSender : IRecoveryEmailSender, IConfirmationEmailSender
{
    public ConcurrentDictionary<string, string> Links { get; } = new();
    public ConcurrentDictionary<string, string> ConfirmationLinks { get; } = new();
    public Task SendConfirmationAsync(string email, string link, CancellationToken cancellationToken)
    {
        ConfirmationLinks[email] = link;
        return Task.CompletedTask;
    }
    public Task SendAsync(string email, string link, CancellationToken cancellationToken)
    {
        Links[email] = link;
        return Task.CompletedTask;
    }
}
