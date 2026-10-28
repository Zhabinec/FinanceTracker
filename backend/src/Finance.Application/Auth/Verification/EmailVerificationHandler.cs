using System.Net.Mail;
using Finance.Application.Abstractions;
using Finance.Application.Auth.Recovery;
using Finance.Domain.Users;

namespace Finance.Application.Auth.Verification;

public interface IEmailVerification
{
    Task RequestAsync(string? email, CancellationToken cancellationToken);
    Task<bool> ConfirmAsync(string? token, CancellationToken cancellationToken);
}
public interface IEmailVerificationStore
{
    Task<User?> IssueAsync(string normalizedEmail, string tokenHash, DateTimeOffset expiresAtUtc, CancellationToken cancellationToken);
    Task<bool> ConfirmAsync(string tokenHash, DateTimeOffset nowUtc, CancellationToken cancellationToken);
}
public interface IConfirmationEmailSender
{
    Task SendConfirmationAsync(string email, string link, CancellationToken cancellationToken);
}

public sealed class EmailVerificationHandler(IEmailVerificationStore store, IRefreshTokenProtector tokens,
    IConfirmationEmailSender sender, IClock clock, PasswordRecoverySettings settings) : IEmailVerification
{
    public async Task RequestAsync(string? email, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 320 || !MailAddress.TryCreate(email, out _)) return;
        var token = tokens.Generate();
        var user = await store.IssueAsync(User.NormalizeEmail(email), tokens.Hash(token), clock.UtcNow.AddHours(24), cancellationToken);
        if (user?.Email is null) return;
        await sender.SendConfirmationAsync(user.Email,
            $"{settings.FrontendUrl.TrimEnd('/')}/confirm-email#token={Uri.EscapeDataString(token)}", cancellationToken);
    }

    public Task<bool> ConfirmAsync(string? token, CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(token) || token.Length > 256 ? Task.FromResult(false) :
            store.ConfirmAsync(tokens.Hash(token), clock.UtcNow, cancellationToken);
}
