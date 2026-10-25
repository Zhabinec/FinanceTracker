using System.Net.Http.Headers;
using System.Net.Http.Json;
using Finance.Application.Auth.Recovery;
using Finance.Application.Auth.Verification;
using Microsoft.Extensions.Logging;
using System.Net;

namespace Finance.Infrastructure.Security;

public sealed record RecoveryEmailOptions(string Provider, string Endpoint, string From, string? ApiKey);

public sealed class RecoveryEmailSender(HttpClient http, RecoveryEmailOptions options, ILogger<RecoveryEmailSender> logger)
    : IRecoveryEmailSender, IConfirmationEmailSender
{
    public Task SendAsync(string email, string link, CancellationToken cancellationToken) => SendMessageAsync(
        email, link, "Восстановление пароля Finance Tracker",
        $"Для аккаунта Finance Tracker с email {email} запрошена смена пароля.",
        "Сменить пароль", "30 минут", cancellationToken);

    public Task SendConfirmationAsync(string email, string link, CancellationToken cancellationToken) => SendMessageAsync(
        email, link, "Подтверждение email Finance Tracker",
        $"Для адреса {email} создан аккаунт Finance Tracker. Подтвердите, что этот email принадлежит вам.",
        "Подтвердить email", "24 часа", cancellationToken);

    private async Task SendMessageAsync(string email, string link, string subject, string introduction,
        string action, string lifetime, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, options.Endpoint);
        var text = $"{introduction}\n\n{action} (ссылка действует {lifetime} и используется один раз):\n{link}\n\nЕсли вы не запрашивали это действие для {email}, проигнорируйте письмо.";
        var html = $"<!doctype html><html lang=\"ru\"><body><h1>{WebUtility.HtmlEncode(subject)}</h1><p>{WebUtility.HtmlEncode(introduction)}</p>" +
            $"<p><a href=\"{WebUtility.HtmlEncode(link)}\">{WebUtility.HtmlEncode(action)}</a></p>" +
            $"<p>Ссылка действует {lifetime} и используется один раз.</p><p>Если вы не запрашивали это действие для {WebUtility.HtmlEncode(email)}, проигнорируйте письмо.</p></body></html>";
        request.Content = options.Provider switch
        {
            "Mailpit" => JsonContent.Create(new { From = new { Email = options.From, Name = "Finance Tracker" }, To = new[] { new { Email = email } }, Subject = subject, Text = text, HTML = html }),
            "Resend" => JsonContent.Create(new { from = options.From, to = new[] { email }, subject, text, html }),
            _ => throw new InvalidOperationException("Email provider is not configured.")
        };
        if (options.Provider == "Resend") request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException) { logger.LogError("Account email delivery failed for {Purpose}.", subject); }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        { logger.LogError("Account email delivery timed out for {Purpose}.", subject); }
    }
}
