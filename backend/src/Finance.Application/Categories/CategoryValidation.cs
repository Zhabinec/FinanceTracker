using Finance.Domain.Transactions;

namespace Finance.Application.Categories;

internal static class CategoryValidation
{
    public static IReadOnlyDictionary<string, string[]> ValidateName(string? name)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(name))
        {
            errors["name"] = ["Category name is required."];
        }
        else if (name.Trim().Length > 100)
        {
            errors["name"] = ["Category name cannot exceed 100 characters."];
        }

        return errors;
    }

    public static IReadOnlyDictionary<string, string[]> ValidateCreate(
        string? name,
        TransactionType type)
    {
        var errors = new Dictionary<string, string[]>(ValidateName(name));

        if (!Enum.IsDefined(type))
        {
            errors["type"] = ["Category type must be Income or Expense."];
        }

        return errors;
    }
}
