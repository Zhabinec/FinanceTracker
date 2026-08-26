using Finance.Domain.Categories;
using Finance.Domain.Transactions;

namespace Finance.Application.Categories;

public sealed record CategoryDto(
    Guid Id,
    string Name,
    TransactionType Type,
    bool IsActive)
{
    public static CategoryDto From(Category category) =>
        new(category.Id, category.Name, category.Type, category.IsActive);
}
