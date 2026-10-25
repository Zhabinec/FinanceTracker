using System.Net.Http.Headers;
using System.Net.Http.Json;
using Finance.Application.Auth.Recovery;

namespace Finance.Infrastructure.Security;

public sealed record RecoveryEmailOptions(string Provider, string Endpoint, string From, string? ApiKey);

public sealed class RecoveryEmailSender(HttpClient http, RecoveryEmailOptions options) : IRecoveryEmailSender
{
    public async Task SendAsync(string email, string link, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, options.Endpoint);
        var text = $"Для смены пароля Finance Tracker откройте ссылку (действует 30 минут):\n{link}\nЕсли вы не запрашивали смену пароля, проигнорируйте письмо.";
        request.Content = options.Provider switch
        {
            "Mailpit" => JsonContent.Create(new { From = new { Email = options.From, Name = "Finance Tracker" }, To = new[] { new { Email = email } }, Subject = "Восстановление пароля Finance Tracker", Text = text }),
            "Resend" => JsonContent.Create(new { from = options.From, to = new[] { email }, subject = "Восстановление пароля Finance Tracker", text }),
            _ => throw new InvalidOperationException("Email provider is not configured.")
        };
        if (options.Provider == "Resend") request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
