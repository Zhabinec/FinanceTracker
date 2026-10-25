using Finance.Api.Contracts.Transactions;
using Finance.Application.Transactions;
using Finance.Application.Transactions.Create;
using Finance.Application.Transactions.Delete;
using Finance.Application.Transactions.GetTransactions;
using Finance.Application.Transactions.Update;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Finance.Api.Controllers;

[ApiController]
[Route("api/transactions")]
[Authorize]
public sealed class TransactionsController(
    CreateTransactionHandler createTransactionHandler,
    GetTransactionsHandler getTransactionsHandler,
    UpdateTransactionHandler updateTransactionHandler,
    DeleteTransactionHandler deleteTransactionHandler) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<TransactionDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var transactions = await getTransactionsHandler.HandleAsync(cancellationToken);
        return transactions is null ? Unauthorized() : Ok(transactions);
    }

    [HttpPost]
    [ProducesResponseType<TransactionDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        CreateTransactionRequest request,
        CancellationToken cancellationToken)
    {
        var result = await createTransactionHandler.HandleAsync(
            new CreateTransactionCommand(
                request.CategoryId,
                request.Type,
                request.Amount,
                request.Description,
                request.OccurredOn),
            cancellationToken);

        return ToActionResult(result, StatusCodes.Status201Created);
    }

    [HttpPut("{transactionId:guid}")]
    [ProducesResponseType<TransactionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        Guid transactionId,
        UpdateTransactionRequest request,
        CancellationToken cancellationToken)
    {
        var result = await updateTransactionHandler.HandleAsync(
            new UpdateTransactionCommand(
                transactionId,
                request.CategoryId,
                request.Type,
                request.Amount,
                request.Description,
                request.OccurredOn),
            cancellationToken);

        return ToActionResult(result, StatusCodes.Status200OK);
    }

    [HttpDelete("{transactionId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(
        Guid transactionId,
        CancellationToken cancellationToken)
    {
        var status = await deleteTransactionHandler.HandleAsync(
            transactionId,
            cancellationToken);

        return status switch
        {
            DeleteTransactionStatus.Success => NoContent(),
            DeleteTransactionStatus.NotFound => NotFoundProblem("Transaction was not found."),
            DeleteTransactionStatus.Unauthorized => Unauthorized(),
            _ => throw new InvalidOperationException("Unknown transaction deletion result.")
        };
    }

    private IActionResult ToActionResult(
        TransactionMutationResult result,
        int successStatusCode)
    {
        return result.Status switch
        {
            TransactionMutationStatus.Success =>
                StatusCode(successStatusCode, result.Transaction),
            TransactionMutationStatus.InvalidInput =>
                BadRequest(new ValidationProblemDetails(
                    result.Errors.ToDictionary(error => error.Key, error => error.Value))
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "One or more validation errors occurred."
                }),
            TransactionMutationStatus.NotFound =>
                NotFoundProblem("Transaction was not found."),
            TransactionMutationStatus.CategoryNotFound =>
                NotFoundProblem("Category was not found."),
            TransactionMutationStatus.CategoryInactive =>
                Conflict(new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = result.Errors["categoryId"][0]
                }),
            TransactionMutationStatus.Unauthorized => Unauthorized(),
            _ => throw new InvalidOperationException("Unknown transaction mutation result.")
        };
    }

    private NotFoundObjectResult NotFoundProblem(string title) =>
        NotFound(new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = title
        });
}
