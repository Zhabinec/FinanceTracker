using Finance.Domain.Transactions;
using System.Text.Json.Serialization;

namespace Finance.Api.Contracts.Transactions;

public sealed record UpdateTransactionRequest(
    Guid CategoryId,
    TransactionType Type,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] decimal Amount,
    string? Description,
    DateOnly? OccurredOn,
    Guid? Version,
    Guid? AccountId = null);
