using Finance.Domain.Common;
using Finance.Domain.Users;

namespace Finance.UnitTests.Domain;

public sealed class UserTests
{
    [Fact]
    public void TelegramUserHasNoInventedEmailOrVerifiedEmail()
    {
        var user = User.CreateTelegram("random-password-hash", DateTimeOffset.UtcNow);
        Assert.Null(user.Email);
        Assert.Null(user.NormalizedEmail);
        Assert.Null(user.EmailConfirmedAtUtc);
        Assert.Equal("RUB", user.BaseCurrency);
        Assert.NotEqual(Guid.Empty, user.Id);
    }
    [Fact]
    public void Create_PreservesEmailAndNormalizesEmailForLookup()
    {
        var user = User.Create(
            " User@Example.com ",
            "password-hash",
            new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero),
            "rub");

        Assert.Equal("User@Example.com", user.Email);
        Assert.Equal("USER@EXAMPLE.COM", user.NormalizedEmail);
        Assert.Equal("RUB", user.BaseCurrency);
    }

    [Fact]
    public void Create_WithNonUtcRegistrationDate_Throws()
    {
        Assert.Throws<DomainException>(() => User.Create(
            "user@example.com",
            "password-hash",
            new DateTimeOffset(2026, 8, 24, 15, 0, 0, TimeSpan.FromHours(3)),
            "RUB"));
    }

    [Fact]
    public void Create_WithNonAsciiCurrencyCode_Throws()
    {
        Assert.Throws<DomainException>(() => User.Create(
            "user@example.com",
            "password-hash",
            new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero),
            "руб"));
    }
}
