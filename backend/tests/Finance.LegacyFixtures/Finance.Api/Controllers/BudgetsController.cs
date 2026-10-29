using System.Text.Json.Serialization;
using Finance.Application.Budgets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Finance.Api.Controllers;

[ApiController, Authorize, Route("api/budgets")]
public sealed class BudgetsController(BudgetHandler handler) : ControllerBase
{
    public sealed record CreateBudget(Guid CategoryId, DateOnly Month,
        [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] decimal Limit);
    public sealed record ChangeBudget([property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] decimal Limit, Guid Version);
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] DateOnly month, CancellationToken cancellationToken) =>
        Ok(await handler.ListAsync(month, cancellationToken));
    [HttpPost]
    public async Task<IActionResult> Create(CreateBudget request, CancellationToken cancellationToken)
    {
        var id = await handler.CreateAsync(request.CategoryId, request.Month, request.Limit, cancellationToken);
        return id.HasValue ? StatusCode(201, new { id = id.Value }) : Missing();
    }
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Change(Guid id, ChangeBudget request, CancellationToken cancellationToken) =>
        await handler.ChangeAsync(id, request.Limit, request.Version, cancellationToken) ? NoContent() : Missing();
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] Guid version, CancellationToken cancellationToken) =>
        await handler.ChangeAsync(id, null, version, cancellationToken) ? NoContent() : Missing();
    private NotFoundObjectResult Missing() => NotFound(new ProblemDetails { Status = 404, Title = "Бюджет или категория не найдены." });
}
