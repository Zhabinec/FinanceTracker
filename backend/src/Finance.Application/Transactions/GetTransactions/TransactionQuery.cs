using Finance.Application.Common;
using Finance.Domain.Transactions;

namespace Finance.Application.Transactions.GetTransactions;

public sealed record TransactionQuery(
    DateOnly? From = null, DateOnly? To = null, Guid? CategoryId = null,
    TransactionType? Type = null, string Sort = "dateDesc", int Page = 1, int PageSize = 20, Guid? AccountId = null)
{
    public void Validate()
    {
        var errors = new Dictionary<string, string[]>();
        if (From > To) errors["period"] = ["Start date cannot be after end date."];
        if (From?.Year is < 1900 or > 9998 || To?.Year is < 1900 or > 9998)
            errors["period"] = ["Dates must be between 1900 and 9998."];
        if (CategoryId == Guid.Empty) errors["categoryId"] = ["Invalid category identifier."];
        if (AccountId == Guid.Empty) errors["accountId"] = ["Invalid account identifier."];
        if (Type.HasValue && !Enum.IsDefined(Type.Value)) errors["type"] = ["Invalid transaction type."];
        if (Sort is not ("dateDesc" or "dateAsc" or "amountDesc" or "amountAsc"))
            errors["sort"] = ["Unsupported sort order."];
        if (Page is < 1 or > 1_000_000) errors["page"] = ["Page must be between 1 and 1000000."];
        if (PageSize is < 1 or > 100) errors["pageSize"] = ["Page size must be between 1 and 100."];
        if (errors.Count > 0) throw new RequestValidationException(errors);
    }
}

public sealed record TransactionPage(IReadOnlyList<TransactionDto> Items, int TotalCount, int Page, int PageSize);
public sealed record TransactionRecordPage(IReadOnlyList<Finance.Application.Abstractions.TransactionRecord> Items, int TotalCount);
