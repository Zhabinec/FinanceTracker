using System.IdentityModel.Tokens.Jwt;
using Finance.Application.Abstractions;

namespace Finance.Api.Security;

public sealed class HttpCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public Guid? UserId
    {
        get
        {
            var subject = httpContextAccessor.HttpContext?.User
                .FindFirst(JwtRegisteredClaimNames.Sub)?
                .Value;

            return Guid.TryParse(subject, out var userId) ? userId : null;
        }
    }
}
