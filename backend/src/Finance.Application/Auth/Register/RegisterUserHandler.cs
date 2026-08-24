using System.Net.Mail;
using Finance.Application.Abstractions;
using Finance.Domain.Users;

namespace Finance.Application.Auth.Register;

public sealed class RegisterUserHandler(
    IUserRegistrationStore userStore,
    IPasswordHasher passwordHasher,
    IClock clock)
{
    public async Task<RegisterUserResult> HandleAsync(
        RegisterUserCommand command,
        CancellationToken cancellationToken)
    {
        var validationErrors = Validate(command);
        if (validationErrors.Count > 0)
        {
            return RegisterUserResult.Invalid(validationErrors);
        }

        var normalizedEmail = User.NormalizeEmail(command.Email);
        if (await userStore.EmailExistsAsync(normalizedEmail, cancellationToken))
        {
            return RegisterUserResult.EmailTaken();
        }

        var user = User.Create(
            command.Email,
            passwordHasher.Hash(command.Password),
            clock.UtcNow,
            command.BaseCurrency);

        if (!await userStore.TryAddAsync(user, cancellationToken))
        {
            return RegisterUserResult.EmailTaken();
        }

        return RegisterUserResult.Success(new RegisteredUserDto(
            user.Id,
            user.Email,
            user.BaseCurrency,
            user.RegisteredAtUtc));
    }

    private static Dictionary<string, string[]> Validate(RegisterUserCommand command)
    {
        var errors = new Dictionary<string, string[]>();
        var email = command.Email?.Trim();

        if (string.IsNullOrWhiteSpace(email) ||
            email.Length > 320 ||
            !MailAddress.TryCreate(email, out var parsedEmail) ||
            !string.Equals(parsedEmail.Address, email, StringComparison.Ordinal))
        {
            errors["email"] = ["A valid email address is required."];
        }

        if (string.IsNullOrEmpty(command.Password) || command.Password.Length < 8)
        {
            errors["password"] = ["Password must contain at least 8 characters."];
        }
        else if (command.Password.Length > 128)
        {
            errors["password"] = ["Password cannot exceed 128 characters."];
        }

        var currency = command.BaseCurrency?.Trim();
        if (currency is null ||
            currency.Length != 3 ||
            currency.Any(character =>
                !((character >= 'A' && character <= 'Z') ||
                  (character >= 'a' && character <= 'z'))))
        {
            errors["baseCurrency"] = ["Base currency must be a three-letter currency code."];
        }

        return errors;
    }
}
