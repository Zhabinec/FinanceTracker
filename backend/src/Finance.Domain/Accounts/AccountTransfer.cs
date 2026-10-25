using Finance.Domain.Common;
using Finance.Domain.Transactions;

namespace Finance.Domain.Accounts;

public sealed class AccountTransfer
{
    private AccountTransfer() { }
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public Guid RequestId { get; private set; }
    public Guid SourceAccountId { get; private set; }
    public Guid TargetAccountId { get; private set; }
    public string Currency { get; private set; } = "";
    public decimal Amount { get; private set; }
    public string? Description { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? CancelledAtUtc { get; private set; }
    public Guid Version { get; private set; } = Guid.NewGuid();

    public static AccountTransfer Create(Guid userId, Guid requestId, Account source, Account target,
        decimal amount, string? description, DateTimeOffset createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        if (userId == Guid.Empty || requestId == Guid.Empty) throw new DomainException("Нужны владелец и идентификатор запроса.");
        if (source.UserId != userId || target.UserId != userId) throw new DomainException("Оба счёта должны принадлежать пользователю.");
        if (source.Id == target.Id) throw new DomainException("Выберите два разных счёта.");
        if (source.IsArchived || target.IsArchived) throw new DomainException("Архивный счёт нельзя использовать для перевода.");
        if (source.Currency != target.Currency) throw new DomainException("Перевод возможен только между счетами в одной валюте.");
        if (amount <= 0 || amount > Transaction.MaxAmount || decimal.Round(amount, 2) != amount)
            throw new DomainException("Укажите положительную сумму с точностью до копеек в пределах decimal(18,2).");
        if (description?.Trim().Length > Transaction.MaxDescriptionLength) throw new DomainException("Описание не должно превышать 500 символов.");
        if (createdAtUtc.Offset != TimeSpan.Zero) throw new DomainException("Дата создания должна быть в UTC.");
        return new AccountTransfer { Id = Guid.NewGuid(), UserId = userId, RequestId = requestId,
            SourceAccountId = source.Id, TargetAccountId = target.Id, Currency = source.Currency,
            Amount = amount, Description = NormalizeDescription(description), CreatedAtUtc = createdAtUtc };
    }

    public static string? NormalizeDescription(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    public void Cancel(DateTimeOffset nowUtc)
    {
        if (nowUtc.Offset != TimeSpan.Zero) throw new DomainException("Дата отмены должна быть в UTC.");
        if (CancelledAtUtc.HasValue) return;
        CancelledAtUtc = nowUtc;
        Version = Guid.NewGuid();
    }
}
