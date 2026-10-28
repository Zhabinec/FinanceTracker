using System.Text.Json.Serialization;
using Finance.Application.Transfers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Finance.Api.Controllers;

[ApiController, Authorize, Route("api/transfers")]
public sealed class TransfersController(TransferHandler handler) : ControllerBase
{
    public sealed record CreateTransfer(Guid RequestId, Guid SourceAccountId, Guid TargetAccountId,
        [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] decimal Amount, string? Description);
    public sealed record CancelTransfer(Guid Version);
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken, [FromQuery] int page = 1, [FromQuery] int pageSize = 20) =>
        Ok(await handler.ListAsync(page, pageSize, cancellationToken));
    [HttpPost]
    public async Task<IActionResult> Create(CreateTransfer request, CancellationToken cancellationToken)
    {
        var result = await handler.CreateAsync(new(request.RequestId, request.SourceAccountId, request.TargetAccountId, request.Amount, request.Description), cancellationToken);
        return result is null ? Missing() : StatusCode(result.Created ? 201 : 200, result.Transfer);
    }
    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancelTransfer request, CancellationToken cancellationToken) =>
        await handler.CancelAsync(id, request.Version, cancellationToken) ? NoContent() : Missing();
    private NotFoundObjectResult Missing() => NotFound(new ProblemDetails { Status = 404, Title = "Перевод или счёт не найден." });
}
