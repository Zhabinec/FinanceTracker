using Finance.Domain.Transactions;

namespace Finance.Application.Categories.Create;

public sealed record CreateCategoryCommand(string? Name, TransactionType Type);
