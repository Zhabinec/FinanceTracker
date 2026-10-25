using Finance.Domain.Transactions;

namespace Finance.Application.Transactions;

internal static class TransactionValidation
{
    public static IReadOnlyDictionary<string, string[]> Validate(
        Guid categoryId,
        TransactionType type,
        decimal amount,
        string? description,
        DateOnly? occurredOn)
    {
        var errors = new Dictionary<string, string[]>();

        if (categoryId == Guid.Empty)
        {
            errors["categoryId"] = ["Category is required."];
        }

        if (!Enum.IsDefined(type))
        {
            errors["type"] = ["Transaction type must be Income or Expense."];
        }

        if (amount <= 0)
        {
            errors["amount"] = ["Transaction amount must be greater than zero."];
        }
        else if (decimal.Round(amount, 2) != amount)
        {
            errors["amount"] = ["Transaction amount cannot have more than two decimal places."];
        }
        else if (amount > Transaction.MaxAmount)
        {
            errors["amount"] = [$"Transaction amount cannot exceed {Transaction.MaxAmount}."];
        }

        if (description?.Trim().Length > Transaction.MaxDescriptionLength)
        {
            errors["description"] =
                [$"Transaction description cannot exceed {Transaction.MaxDescriptionLength} characters."];
        }

        if (occurredOn is null)
        {
            errors["occurredOn"] = ["Transaction date is required."];
        }
        else if (occurredOn.Value.Year is < 1900 or > 9998)
        {
            errors["occurredOn"] = ["Transaction date must be between 1900 and 9998."];
        }

        return errors;
    }
}
