using Finance.Application.Budgets;
using Finance.Application.Dashboard;
using Finance.Application.FinancialRecords;
using Finance.Application.Messaging;
using Finance.Application.Transfers;
using Finance.Domain.Ledger;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Finance.Api.Controllers;

[ApiController, Authorize(Policy = "server-finance"), Route("api/finance"), RequestSizeLimit(16_384)]
public sealed class FinancialReportsController : ControllerBase
{
    [HttpGet("dashboard")]
    public Task<DashboardDto> Dashboard([FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromServices] RequestDispatcher<ReadFinancialDashboard, DashboardDto> dispatcher, CancellationToken ct) => dispatcher.SendAsync(new(from, to), ct);
    [HttpGet("budgets")]
    public Task<IReadOnlyList<BudgetDto>> Budgets([FromQuery] DateOnly month,
        [FromServices] RequestDispatcher<ListFinancialBudgets, IReadOnlyList<BudgetDto>> dispatcher, CancellationToken ct) => dispatcher.SendAsync(new(month), ct);
    [HttpPost("budgets")]
    public Task<BudgetData> CreateBudget(BudgetInput input, [FromServices] RequestDispatcher<SaveFinancialBudget, BudgetData> dispatcher, CancellationToken ct) =>
        dispatcher.SendAsync(new(null, input.CategoryId, input.Month, input.Limit, null), ct);
    [HttpPut("budgets/{id:guid}")]
    public Task<BudgetData> UpdateBudget(Guid id, BudgetInput input, [FromServices] RequestDispatcher<SaveFinancialBudget, BudgetData> dispatcher, CancellationToken ct) =>
        dispatcher.SendAsync(new(id, Guid.Empty, default, input.Limit, input.Version), ct);
    [HttpDelete("budgets/{id:guid}")]
    public async Task<IActionResult> DeleteBudget(Guid id, [FromQuery] Guid version, [FromServices] RequestDispatcher<DeleteFinancialBudget, bool> dispatcher, CancellationToken ct)
    { await dispatcher.SendAsync(new(id, version), ct); return NoContent(); }
    [HttpGet("transfers")]
    public Task<TransferPage> Transfers([FromServices] RequestDispatcher<ListFinancialTransfers, TransferPage> dispatcher, CancellationToken ct,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 10) => dispatcher.SendAsync(new(page, pageSize), ct);
    [HttpPost("transfers")]
    public Task<TransferDto> CreateTransfer(CreateFinancialTransfer input, [FromServices] RequestDispatcher<CreateFinancialTransfer, TransferDto> dispatcher, CancellationToken ct) => dispatcher.SendAsync(input, ct);
    [HttpPost("transfers/{id:guid}/cancel")]
    public async Task<IActionResult> CancelTransfer(Guid id, FinancialCatalogController.VersionInput input,
        [FromServices] RequestDispatcher<CancelFinancialTransfer, bool> dispatcher, CancellationToken ct)
    { await dispatcher.SendAsync(new(id, input.Version), ct); return NoContent(); }
    public sealed record BudgetInput(Guid CategoryId = default, DateOnly Month = default, decimal Limit = 0, Guid? Version = null);
}
