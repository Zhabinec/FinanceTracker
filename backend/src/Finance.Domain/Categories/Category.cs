using Finance.Domain.Common;
using Finance.Domain.Transactions;

namespace Finance.Domain.Categories;

public sealed class Category
{
    private Category()
    {
    }

    private Category(Guid id, Guid userId, string name, TransactionType type)
    {
        Id = id;
        UserId = userId;
        Name = name;
        Type = type;
        IsActive = true;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public TransactionType Type { get; private set; }

    public bool IsActive { get; private set; }

    public static Category Create(Guid userId, string name, TransactionType type)
    {
        if (userId == Guid.Empty)
        {
            throw new DomainException("Category owner is required.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Category name is required.");
        }

        if (name.Trim().Length > 100)
        {
            throw new DomainException("Category name cannot exceed 100 characters.");
        }

        if (!Enum.IsDefined(type))
        {
            throw new DomainException("Category type is invalid.");
        }

        return new Category(Guid.NewGuid(), userId, name.Trim(), type);
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Category name is required.");
        }

        if (name.Trim().Length > 100)
        {
            throw new DomainException("Category name cannot exceed 100 characters.");
        }

        Name = name.Trim();
    }

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;
}
