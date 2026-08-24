using Finance.Application.Users.GetCurrent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Finance.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize]
public sealed class UsersController(GetCurrentUserHandler getCurrentUserHandler) : ControllerBase
{
    [HttpGet("me")]
    [ProducesResponseType<CurrentUserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetCurrent(CancellationToken cancellationToken)
    {
        var user = await getCurrentUserHandler.HandleAsync(cancellationToken);
        return user is null ? Unauthorized() : Ok(user);
    }
}
