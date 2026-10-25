using System.Net;
using System.Text.Json;
using Finance.Infrastructure.Security;
using Microsoft.Extensions.Logging.Abstractions;

namespace Finance.IntegrationTests;

public sealed class AccountEmailTests
{
    [Theory]
    [InlineData("Mailpit")]
    [InlineData("Resend")]
    public async Task Messages_IdentifyAccount_AndPreserveClickableFragmentLink(string provider)
    {
        var handler = new Capture();
        using var http = new HttpClient(handler);
        var sender = new RecoveryEmailSender(http, new RecoveryEmailOptions(provider,
            "https://mail.example.test/send", "finance@example.test", "test-key"), NullLogger<RecoveryEmailSender>.Instance);
        const string email = "person+tag@example.com";
        const string link = "https://finance.example.test/reset-password#token=a%2Bb%2Fc%3D";
        await sender.SendAsync(email, link, CancellationToken.None);
        var json = JsonDocument.Parse(handler.Body!).RootElement;
        Assert.Contains(email, json.GetProperty("text").GetString());
        Assert.Contains(link, json.GetProperty("text").GetString());
        Assert.Contains($"href=\"{link}\"", json.GetProperty("html").GetString());
        await sender.SendConfirmationAsync(email, link.Replace("reset-password", "confirm-email"), CancellationToken.None);
        json = JsonDocument.Parse(handler.Body!).RootElement;
        Assert.Contains(email, json.GetProperty("text").GetString());
        Assert.Contains("confirm-email#token=", json.GetProperty("html").GetString());
    }

    private sealed class Capture : HttpMessageHandler
    {
        public string? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
