using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Finance.Application.Abstractions;
using Finance.Domain.Users;
using Microsoft.IdentityModel.Tokens;

namespace Finance.Api.Security;

public sealed class JwtAccessTokenProvider(
    JwtOptions options,
    IClock clock) : IAccessTokenProvider
{
    public AccessToken Create(User user)
    {
        var issuedAtUtc = clock.UtcNow;
        var expiresAtUtc = issuedAtUtc.AddMinutes(options.AccessTokenLifetimeMinutes);
        var signingKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(options.SigningKey));

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim("security_stamp", user.SecurityStamp.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

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
