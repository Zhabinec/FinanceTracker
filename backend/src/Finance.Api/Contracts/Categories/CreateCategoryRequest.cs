using Finance.Domain.Transactions;

namespace Finance.Api.Contracts.Categories;

public sealed record CreateCategoryRequest(string? Name, TransactionType Type);
