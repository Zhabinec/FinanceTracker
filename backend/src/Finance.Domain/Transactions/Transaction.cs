using Finance.Domain.Categories;
using Finance.Domain.Common;

namespace Finance.Domain.Transactions;

public sealed class Transaction
{
    private Transaction()
    {
    }

    private Transaction(
        Guid id,
        Guid userId,
        Guid categoryId,
        TransactionType type,
        decimal amount,
        string? description,
        DateTimeOffset occurredAtUtc,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        UserId = userId;
        CategoryId = categoryId;
        Type = type;
        Amount = amount;
        Description = description;
        OccurredAtUtc = occurredAtUtc;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public Guid CategoryId { get; private set; }

    public TransactionType Type { get; private set; }

    public decimal Amount { get; private set; }

    public string? Description { get; private set; }

    public DateTimeOffset OccurredAtUtc { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static Transaction Create(
        Guid userId,
        Category category,
        TransactionType type,
        decimal amount,
        string? description,
        DateTimeOffset occurredAtUtc,
        DateTimeOffset createdAtUtc)
    {
        Validate(userId, category, type, amount, description, occurredAtUtc, createdAtUtc);

        return new Transaction(
            Guid.NewGuid(),
            userId,
            category.Id,
            type,
            amount,
            NormalizeDescription(description),
            occurredAtUtc,
            createdAtUtc);
    }

    private static void Validate(
        Guid userId,
        Category category,
        TransactionType type,
        decimal amount,
        string? description,
        DateTimeOffset occurredAtUtc,
        DateTimeOffset createdAtUtc)
    {
        if (userId == Guid.Empty)
        {
            throw new DomainException("Transaction owner is required.");
        }

        ArgumentNullException.ThrowIfNull(category);

        if (category.UserId != userId)
        {
            throw new DomainException("The category belongs to another user.");
        }

        if (!category.IsActive)
        {
            throw new DomainException("An inactive category cannot be used for a new transaction.");
        }

        if (category.Type != type)
        {
            throw new DomainException("Category type must match transaction type.");
        }

        if (amount <= 0)
        {
            throw new DomainException("Transaction amount must be greater than zero.");
        }

        if (decimal.Round(amount, 2) != amount)
        {
            throw new DomainException("Transaction amount cannot have more than two decimal places.");
        }

        if (description?.Trim().Length > 500)
        {
            throw new DomainException("Transaction description cannot exceed 500 characters.");
        }

        EnsureUtc(occurredAtUtc, nameof(occurredAtUtc));
        EnsureUtc(createdAtUtc, nameof(createdAtUtc));
    }

    private static string? NormalizeDescription(string? description)
    {
        return string.IsNullOrWhiteSpace(description) ? null : description.Trim();
    }

    private static void EnsureUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new DomainException($"{parameterName} must be in UTC.");
        }
    }
}
