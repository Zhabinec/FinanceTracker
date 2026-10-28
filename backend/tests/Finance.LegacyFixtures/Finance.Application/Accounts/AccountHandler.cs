using System.Globalization;
using Finance.Application.Abstractions;
using Finance.Application.Common;
using Finance.Domain.Accounts;

namespace Finance.Application.Accounts;

public sealed record AccountBalance(Account Account, decimal Balance)
{
    public AccountDto ToDto() => new(Account.Id, Account.Name, Account.Currency,
        Account.OpeningBalance.ToString("F2", CultureInfo.InvariantCulture), Balance.ToString("F2", CultureInfo.InvariantCulture), Account.IsArchived, Account.Version);
}

public interface IAccountStore
{
    Task<IReadOnlyList<AccountBalance>> ListAsync(Guid userId, CancellationToken cancellationToken);
    Task<Account?> FindOwnedAsync(Guid id, Guid userId, CancellationToken cancellationToken);
    void Add(Account account);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed class AccountHandler(ICurrentUser currentUser, IAccountStore store, IUserProfileStore profiles)
{
    private Guid UserId => currentUser.UserId ?? throw new UnauthorizedAccessException();

    public async Task<IReadOnlyList<AccountDto>> ListAsync(CancellationToken cancellationToken) =>
        (await store.ListAsync(UserId, cancellationToken)).Select(item => item.ToDto()).ToArray();

    public async Task<AccountDto> CreateAsync(string name, decimal openingBalance, CancellationToken cancellationToken)
    {
        var user = await profiles.FindByIdAsync(UserId, cancellationToken) ?? throw new UnauthorizedAccessException();
        var account = Account.Create(user.Id, name, user.BaseCurrency, openingBalance);
        store.Add(account);
        await store.SaveChangesAsync(cancellationToken);
        return new AccountBalance(account, openingBalance).ToDto();
    }

    public async Task<bool> ChangeAsync(Guid id, string name, bool isArchived, Guid version, CancellationToken cancellationToken)
    {
        var account = await store.FindOwnedAsync(id, UserId, cancellationToken);
        if (account is null) return false;
        if (version == Guid.Empty || account.Version != version)
            throw new ConflictException("Счёт уже изменён. Обновите список перед сохранением.");
        account.Change(name, isArchived);
        await store.SaveChangesAsync(cancellationToken);
        return true;
    }
}
