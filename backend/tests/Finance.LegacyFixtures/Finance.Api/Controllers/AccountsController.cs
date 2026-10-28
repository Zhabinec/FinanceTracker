using System.Text.Json.Serialization;
using Finance.Application.Accounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Finance.Api.Controllers;

[ApiController, Authorize, Route("api/accounts")]
public sealed class AccountsController(AccountHandler handler) : ControllerBase
{
    public sealed record CreateAccount(string Name,
        [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] decimal OpeningBalance);
    public sealed record ChangeAccount(string Name, bool IsArchived, Guid Version);

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken) => Ok(await handler.ListAsync(cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Create(CreateAccount request, CancellationToken cancellationToken) =>
        StatusCode(201, await handler.CreateAsync(request.Name, request.OpeningBalance, cancellationToken));

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Change(Guid id, ChangeAccount request, CancellationToken cancellationToken) =>
        await handler.ChangeAsync(id, request.Name, request.IsArchived, request.Version, cancellationToken)
            ? NoContent() : NotFound(new ProblemDetails { Status = 404, Title = "Счёт не найден." });
}
