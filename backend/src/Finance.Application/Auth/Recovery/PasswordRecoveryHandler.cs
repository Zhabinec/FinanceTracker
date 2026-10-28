using System.Net.Mail;
using Finance.Application.Abstractions;
using Finance.Application.Common;
using Finance.Domain.Users;

namespace Finance.Application.Auth.Recovery;

public interface IPasswordRecoveryStore
{
    Task<User?> IssueAsync(string normalizedEmail, string tokenHash, DateTimeOffset expiresAtUtc, CancellationToken cancellationToken);
    Task<bool> ResetAsync(string tokenHash, string passwordHash, DateTimeOffset nowUtc, CancellationToken cancellationToken);
}

public interface IRecoveryEmailSender
{
    Task SendAsync(string email, string link, CancellationToken cancellationToken);
}

public sealed record PasswordRecoverySettings(string FrontendUrl);

public sealed class PasswordRecoveryHandler(IPasswordRecoveryStore store, IRefreshTokenProtector tokens,
    IRecoveryEmailSender sender, IPasswordHasher passwords, IClock clock, PasswordRecoverySettings settings)
{
    public async Task RequestAsync(string? email, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 320 || !MailAddress.TryCreate(email, out _)) return;
        var token = tokens.Generate();
        var user = await store.IssueAsync(User.NormalizeEmail(email), tokens.Hash(token), clock.UtcNow.AddMinutes(30), cancellationToken);
        if (user?.Email is null) return;
        // Fragment keeps the secret out of HTTP access logs and referrer headers.
        var link = $"{settings.FrontendUrl.TrimEnd('/')}/reset-password#token={Uri.EscapeDataString(token)}";
        await sender.SendAsync(user.Email, link, cancellationToken);
    }

    public async Task<bool> ResetAsync(string? token, string? password, CancellationToken cancellationToken)
    {
        if (password is null || password.Length is < 8 or > 128)
            throw new RequestValidationException(new Dictionary<string, string[]> { ["password"] = ["Password must contain 8 to 128 characters."] });
        if (string.IsNullOrWhiteSpace(token) || token.Length > 256) return false;
        return await store.ResetAsync(tokens.Hash(token), passwords.Hash(password), clock.UtcNow, cancellationToken);
    }
}
