using Finance.Domain.Common;
using Finance.Domain.Transactions;

namespace Finance.Domain.Accounts;

public sealed class Account
{
    private Account() { }
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string Name { get; private set; } = "";
    public string NormalizedName { get; private set; } = "";
    public string Currency { get; private set; } = "";
    public decimal OpeningBalance { get; private set; }
    public bool IsArchived { get; private set; }
    public Guid Version { get; private set; } = Guid.NewGuid();

    // The primary account uses the owner's id, including when migrating old history.
    public static Account CreatePrimary(Guid userId, string currency) => Create(userId, "Основной счёт", currency, 0, userId);

    public static Account Create(Guid userId, string name, string currency, decimal openingBalance, Guid? id = null)
    {
        if (userId == Guid.Empty || id == Guid.Empty) throw new DomainException("Укажите владельца счёта.");
        if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3 || currency.Any(c => c is < 'A' or > 'Z'))
            throw new DomainException("Нужен трёхбуквенный код валюты.");
        if (openingBalance > Transaction.MaxAmount || openingBalance < -Transaction.MaxAmount || decimal.Round(openingBalance, 2) != openingBalance)
            throw new DomainException("Начальный остаток должен содержать не больше двух знаков после запятой и помещаться в decimal(18,2).");
        var account = new Account { Id = id ?? Guid.NewGuid(), UserId = userId, Currency = currency, OpeningBalance = openingBalance };
        account.Change(name, false);
        return account;
    }

    public void Change(string name, bool isArchived)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100)
            throw new DomainException("Название счёта должно содержать от 1 до 100 символов.");
        Name = name.Trim();
        NormalizedName = Name.ToUpperInvariant();
        IsArchived = isArchived;
        Version = Guid.NewGuid();
    }
}
