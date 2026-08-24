using Finance.Domain.Common;
using Finance.Domain.Users;

namespace Finance.UnitTests.Domain;

public sealed class RefreshSessionTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_WithExpirationBeforeCreation_Throws()
    {
        Assert.Throws<DomainException>(() => RefreshSession.Create(
            Guid.NewGuid(), new string('A', 64), Now, Now));
    }

    [Fact]
    public void Revoke_Twice_PreservesFirstRevocationTime()
    {
        var session = RefreshSession.Create(Guid.NewGuid(), new string('A', 64), Now, Now.AddDays(30));
        session.Revoke(Now.AddMinutes(1));
        session.Revoke(Now.AddMinutes(2));

        Assert.Equal(Now.AddMinutes(1), session.RevokedAtUtc);
    }
}
