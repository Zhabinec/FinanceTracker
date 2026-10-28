using System.Net;
using System.Net.Http.Json;
using Finance.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Finance.IntegrationTests;

public sealed partial class FinanceApiTests
{
    [Fact]
    public async Task DisabledEmailRegistrationRejectsDirectRequestsWithoutCreatingUserOrEmail()
    {
        using var app = fixture.App.WithWebHostBuilder(builder => builder.UseSetting("Auth:EmailRegistrationEnabled", "false"));
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new("https://localhost") });
        var email = $"disabled-{Guid.NewGuid():N}@example.com";
        Assert.False((await Json(await client.GetAsync("/api/auth/options"))).GetProperty("emailRegistrationEnabled").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/auth/register", new { email, password = "Test-password-123!" })).StatusCode);
        using var scope = app.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<FinanceDbContext>().Users.AnyAsync(x => x.Email == email));
        Assert.False(fixture.Emails.ConfirmationLinks.ContainsKey(email));
    }

    [Fact]
    public async Task DisabledEmailRegistrationPreservesExistingPasswordLogin()
    {
        var email = $"existing-{Guid.NewGuid():N}@example.com";
        using var setup = fixture.Client();
        Assert.Equal(HttpStatusCode.Created, (await setup.PostAsJsonAsync("/api/auth/register", new { email, password = "Test-password-123!" })).StatusCode);
        var token = Uri.UnescapeDataString(fixture.Emails.ConfirmationLinks[email].Split("#token=")[1]);
        Assert.Equal(HttpStatusCode.NoContent, (await setup.PostAsJsonAsync("/api/auth/confirm-email", new { token })).StatusCode);
        using var app = fixture.App.WithWebHostBuilder(builder => builder.UseSetting("Auth:EmailRegistrationEnabled", "false"));
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new("https://localhost") });
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Test-password-123!" })).StatusCode);
    }

    [Fact]
    public async Task ResendDisablesEmailRegistrationByDefault()
    {
        using var app = fixture.App.WithWebHostBuilder(builder => builder.UseSetting("Email:Provider", "Resend"));
        using var client = app.CreateClient();
        Assert.False((await Json(await client.GetAsync("/api/auth/options"))).GetProperty("emailRegistrationEnabled").GetBoolean());
    }
}
