using System.Net;
using Microsoft.AspNetCore.Hosting;

namespace Finance.IntegrationTests;

public sealed partial class FinanceApiTests
{
    [Fact]
    public async Task CombinedHosting_ServesSpaRoutesAndAssets_WithoutMaskingApiErrors()
    {
        var root = Path.Combine(Path.GetTempPath(), "finance-web-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "assets"));
        await File.WriteAllTextAsync(Path.Combine(root, "index.html"), "<!doctype html><title>Finance test shell</title>");
        await File.WriteAllTextAsync(Path.Combine(root, "assets", "test-hash.js"), "console.log('test');");
        try
        {
            await using var app = fixture.App.WithWebHostBuilder(builder =>
            {
                builder.UseWebRoot(root);
                builder.UseSetting("Hosting:ServeFrontend", "true");
                builder.UseSetting("Hosting:UseRenderExternalUrl", "true");
                builder.UseSetting("RENDER_EXTERNAL_URL", "https://finance-test.onrender.com");
            });
            using var client = app.CreateClient();
            foreach (var path in new[] { "/", "/login", "/categories", "/reset-password" })
            {
                var response = await client.GetAsync(path);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Contains("Finance test shell", await response.Content.ReadAsStringAsync());
                Assert.Equal("no-cache", response.Headers.CacheControl?.ToString());
                Assert.Contains("https://web.telegram.org", response.Headers.GetValues("Content-Security-Policy").Single());
                Assert.False(response.Headers.Contains("X-Frame-Options"));
            }
            var asset = await client.GetAsync("/assets/test-hash.js");
            Assert.Equal(HttpStatusCode.OK, asset.StatusCode);
            Assert.Contains("immutable", asset.Headers.CacheControl?.ToString());
            foreach (var path in new[] { "/api/no-such-route", "/assets/missing.js", "/health/missing" })
                Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/not-an-api", null)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/users/me")).StatusCode);
        }
        finally { Directory.Delete(root, true); }
    }
}
