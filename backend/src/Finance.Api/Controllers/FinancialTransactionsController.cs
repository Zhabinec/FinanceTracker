using Finance.Application.Abstractions;
using Finance.Application.FinancialRecords;
using Finance.Application.Messaging;
using Finance.Application.Transactions;
using Finance.Application.Transactions.GetTransactions;
using Finance.Domain.Transactions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Finance.Api.Controllers;

[ApiController, Authorize(Policy = "server-finance"), Route("api/finance"), RequestSizeLimit(16_384)]
public sealed class FinancialTransactionsController : ControllerBase
{
    [HttpGet("status")]
    public async Task<FinancialBookStatus> Status([FromServices] ICurrentUser user,
        [FromServices] IFinancialBookMigration migration, CancellationToken ct) =>
        await migration.StatusAsync(user.UserId ?? throw new UnauthorizedAccessException(), ct);

    [HttpPost("migrate")]
    public Task<FinancialBookStatus> Migrate(MigrateFinancialBook request,
        [FromServices] RequestDispatcher<MigrateFinancialBook, FinancialBookStatus> dispatcher, CancellationToken ct) => dispatcher.SendAsync(request, ct);

    [HttpGet("transactions")]
    public Task<TransactionPage> List([FromQuery] TransactionQuery query,
        [FromServices] RequestDispatcher<ListFinancialTransactions, TransactionPage> dispatcher, CancellationToken ct) =>
        dispatcher.SendAsync(new(query), ct);

    [HttpPost("transactions")]
    public Task<TransactionDto> Create(TransactionInput input,
        [FromServices] RequestDispatcher<SaveFinancialTransaction, TransactionDto> dispatcher, CancellationToken ct) =>
        dispatcher.SendAsync(new(null, input.AccountId, input.CategoryId, input.Type, input.Amount,
            input.Description, input.OccurredOn, input.RequestId, null), ct);

    [HttpPut("transactions/{id:guid}")]
    public Task<TransactionDto> Update(Guid id, TransactionInput input,
        [FromServices] RequestDispatcher<SaveFinancialTransaction, TransactionDto> dispatcher, CancellationToken ct) =>
        dispatcher.SendAsync(new(id, input.AccountId, input.CategoryId, input.Type, input.Amount,
            input.Description, input.OccurredOn, input.RequestId, input.Version), ct);

    [HttpDelete("transactions/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] Guid version,
        [FromServices] RequestDispatcher<DeleteFinancialTransaction, bool> dispatcher, CancellationToken ct)
    { await dispatcher.SendAsync(new(id, version), ct); return NoContent(); }

    public sealed record TransactionInput(Guid AccountId, Guid CategoryId, TransactionType Type, decimal Amount,
        string? Description, DateOnly? OccurredOn, Guid RequestId, Guid? Version);
}
