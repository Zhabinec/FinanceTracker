using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Finance.Application.Abstractions;
using Finance.Domain.Users;
using Finance.Application.Auth.Telegram;
using Microsoft.IdentityModel.Tokens;

namespace Finance.Api.Security;

public sealed class JwtAccessTokenProvider(
    JwtOptions options,
    IClock clock) : IAccessTokenProvider, ITelegramAccessTokenProvider
{
    public AccessToken Create(User user) => Create(user, null);
    public AccessToken CreateTelegram(User user, Guid linkVersion) => Create(user, linkVersion);
    private AccessToken Create(User user, Guid? linkVersion)
    {
        var issuedAtUtc = clock.UtcNow;
        var expiresAtUtc = issuedAtUtc.AddMinutes(options.AccessTokenLifetimeMinutes);
        var signingKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(options.SigningKey));

        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim("security_stamp", user.SecurityStamp.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        if (user.Email is not null) claims.Add(new Claim(JwtRegisteredClaimNames.Email, user.Email));
        if (linkVersion is not null) claims.Add(new Claim("telegram_link", linkVersion.Value.ToString()));

        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims: claims,
            notBefore: issuedAtUtc.UtcDateTime,
            expires: expiresAtUtc.UtcDateTime,
            signingCredentials: new SigningCredentials(
                signingKey,
                SecurityAlgorithms.HmacSha256));

        return new AccessToken(
            new JwtSecurityTokenHandler().WriteToken(token),
            expiresAtUtc);
    }
}
