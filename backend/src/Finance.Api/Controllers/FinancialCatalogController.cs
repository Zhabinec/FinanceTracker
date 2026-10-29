using Finance.Application.Accounts;
using Finance.Application.FinancialRecords;
using Finance.Application.Messaging;
using Finance.Domain.Ledger;
using Finance.Domain.Transactions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Finance.Api.Controllers;

[ApiController, Authorize(Policy = "server-finance"), Route("api/finance"), RequestSizeLimit(16_384)]
public sealed class FinancialCatalogController : ControllerBase
{
    [HttpGet("accounts")]
    public Task<IReadOnlyList<AccountDto>> Accounts([FromServices] RequestDispatcher<ListFinancialAccounts, IReadOnlyList<AccountDto>> dispatcher, CancellationToken ct) => dispatcher.SendAsync(new(), ct);
    [HttpPost("accounts")]
    public Task<AccountDto> CreateAccount(AccountInput input, [FromServices] RequestDispatcher<SaveFinancialAccount, AccountDto> dispatcher, CancellationToken ct) =>
        dispatcher.SendAsync(new(null, input.Name, input.OpeningBalance, false, null), ct);
    [HttpPut("accounts/{id:guid}")]
    public Task<AccountDto> ChangeAccount(Guid id, AccountInput input, [FromServices] RequestDispatcher<SaveFinancialAccount, AccountDto> dispatcher, CancellationToken ct) =>
        dispatcher.SendAsync(new(id, input.Name, 0, input.IsArchived, input.Version), ct);
    [HttpGet("categories")]
    public Task<IReadOnlyList<CategoryData>> Categories([FromServices] RequestDispatcher<ListFinancialCategories, IReadOnlyList<CategoryData>> dispatcher, CancellationToken ct) => dispatcher.SendAsync(new(), ct);
    [HttpPost("categories")]
    public Task<CategoryData> CreateCategory(CategoryInput input, [FromServices] RequestDispatcher<SaveFinancialCategory, CategoryData> dispatcher, CancellationToken ct) =>
        dispatcher.SendAsync(new(null, input.Name, input.Type, true, null), ct);
    [HttpPatch("categories/{id:guid}")]
    public Task<CategoryData> RenameCategory(Guid id, CategoryInput input, [FromServices] RequestDispatcher<SaveFinancialCategory, CategoryData> dispatcher, CancellationToken ct) =>
        dispatcher.SendAsync(new(id, input.Name, null, null, input.Version), ct);
    [HttpPost("categories/{id:guid}/deactivate")]
    public Task<CategoryData> DeactivateCategory(Guid id, VersionInput input, [FromServices] RequestDispatcher<SaveFinancialCategory, CategoryData> dispatcher, CancellationToken ct) =>
        dispatcher.SendAsync(new(id, null, null, false, input.Version), ct);
    [HttpPost("categories/{id:guid}/restore")]
    public Task<CategoryData> RestoreCategory(Guid id, VersionInput input, [FromServices] RequestDispatcher<SaveFinancialCategory, CategoryData> dispatcher, CancellationToken ct) =>
        dispatcher.SendAsync(new(id, null, null, true, input.Version), ct);
    public sealed record AccountInput(string Name, decimal OpeningBalance = 0, bool IsArchived = false, Guid? Version = null);
    public sealed record CategoryInput(string Name, TransactionType? Type, Guid? Version);
    public sealed record VersionInput(Guid Version);
}
