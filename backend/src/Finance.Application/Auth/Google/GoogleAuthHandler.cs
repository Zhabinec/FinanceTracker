using System.Net.Mail;
using Finance.Application.Abstractions;
using Finance.Application.Auth.Refresh;
using Finance.Domain.Users;

namespace Finance.Application.Auth.Google;

// Only the API's validated OIDC principal may construct this proof.
public sealed record GoogleProof(string Subject, string Email, bool EmailVerified);
public sealed record GoogleLinkOwner(Guid UserId, Guid SecurityStamp);
public sealed record GoogleSession(string RefreshToken, DateTimeOffset ExpiresAtUtc);
public interface IGoogleAuthStore
{
    Task<User> OpenAsync(GoogleProof proof, GoogleLinkOwner? link, string passwordHash, string tokenHash,
        DateTimeOffset now, DateTimeOffset expires, CancellationToken cancellationToken);
}
public sealed class GoogleAuthHandler(IGoogleAuthStore store, IPasswordHasher passwords,
    IRefreshTokenProtector tokens, IClock clock, RefreshTokenSettings settings)
{
    public async Task<GoogleSession> LoginAsync(GoogleProof proof, GoogleLinkOwner? link, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(proof.Subject) || proof.Subject.Length > 255 || !proof.EmailVerified ||
            proof.Email.Length > 320 || !MailAddress.TryCreate(proof.Email, out var address) || address.Address != proof.Email)
            throw new UnauthorizedAccessException("Google did not supply a verified identity.");
        if (link is not null && (link.UserId == Guid.Empty || link.SecurityStamp == Guid.Empty))
            throw new UnauthorizedAccessException();
        var refresh = tokens.Generate();
        var now = clock.UtcNow;
        var expires = now.AddDays(settings.LifetimeDays);
        // A new OAuth account has no user-known password. Email recovery may set one later.
        await store.OpenAsync(proof, link, passwords.Hash(tokens.Generate()), tokens.Hash(refresh), now, expires, ct);
        return new(refresh, expires);
    }
}
