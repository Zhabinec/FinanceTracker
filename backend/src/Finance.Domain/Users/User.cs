using Finance.Domain.Common;

namespace Finance.Domain.Users;

public sealed class User
{
    private User()
    {
    }

    private User(
        Guid id,
        string email,
        string normalizedEmail,
        string passwordHash,
        DateTimeOffset registeredAtUtc,
        string baseCurrency)
    {
        Id = id;
        Email = email;
        NormalizedEmail = normalizedEmail;
        PasswordHash = passwordHash;
        RegisteredAtUtc = registeredAtUtc;
        BaseCurrency = baseCurrency;
    }

    public Guid Id { get; private set; }

    public string Email { get; private set; } = string.Empty;

    public string NormalizedEmail { get; private set; } = string.Empty;

    public string PasswordHash { get; private set; } = string.Empty;
    public Guid SecurityStamp { get; private set; } = Guid.NewGuid();

    public void ChangePassword(string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash) || passwordHash.Length > 500)
            throw new DomainException("A valid password hash is required.");
        PasswordHash = passwordHash;
        SecurityStamp = Guid.NewGuid();
    }

    public DateTimeOffset RegisteredAtUtc { get; private set; }

    public string BaseCurrency { get; private set; } = string.Empty;

    public static User Create(
        string email,
        string passwordHash,
        DateTimeOffset registeredAtUtc,
        string baseCurrency)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new DomainException("User email is required.");
        }

        if (email.Trim().Length > 320)
        {
            throw new DomainException("User email cannot exceed 320 characters.");
        }

        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new DomainException("Password hash is required.");
        }

        if (passwordHash.Length > 500)
        {
            throw new DomainException("Password hash cannot exceed 500 characters.");
        }

        EnsureUtc(registeredAtUtc, nameof(registeredAtUtc));

        if (string.IsNullOrWhiteSpace(baseCurrency))
        {
            throw new DomainException("Base currency is required.");
        }

        var normalizedCurrency = baseCurrency.Trim().ToUpperInvariant();
        if (normalizedCurrency.Length != 3 ||
            normalizedCurrency.Any(character => character is < 'A' or > 'Z'))
        {
            throw new DomainException("Base currency must be a three-letter currency code.");
        }

        return new User(
            Guid.NewGuid(),
            email.Trim(),
            NormalizeEmail(email),
            passwordHash,
            registeredAtUtc,
            normalizedCurrency);
    }

    public static string NormalizeEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new DomainException("User email is required.");
        }

        return email.Trim().ToUpperInvariant();
    }

    private static void EnsureUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new DomainException($"{parameterName} must be in UTC.");
        }
    }
}
