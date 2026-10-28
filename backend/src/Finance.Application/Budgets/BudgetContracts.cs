using Finance.Application.Abstractions;
using Finance.Application.Common;
using Finance.Domain.Budgets;

namespace Finance.Application.Budgets;

public sealed record BudgetDto(Guid Id, Guid CategoryId, string CategoryName, bool CategoryActive,
    DateOnly Month, string Limit, string Spent, string Remaining, string Overspent, Guid Version);
