using Finance.Application.Abstractions;
using Finance.Application.Accounts;
using Finance.Application.Common;
using Finance.Application.Messaging;
using Finance.Domain.Categories;
using Finance.Domain.Common;
using Finance.Domain.Ledger;
using Finance.Domain.Transactions;

namespace Finance.Application.FinancialRecords;

public sealed record ListFinancialAccounts : IRequest<IReadOnlyList<AccountDto>>, IAuthenticatedRequest;
public sealed record ListFinancialCategories : IRequest<IReadOnlyList<CategoryData>>, IAuthenticatedRequest;
public sealed record SaveFinancialAccount(Guid? Id, string Name, decimal OpeningBalance, bool IsArchived, Guid? Version)
    : IRequest<AccountDto>, IAuthenticatedRequest;
public sealed record SaveFinancialCategory(Guid? Id, string? Name, TransactionType? Type, bool? IsActive, Guid? Version)
    : IRequest<CategoryData>, IAuthenticatedRequest;

internal static class FinancialVersions
{
    public static void Match(Guid current, Guid? expected)
    { if (expected is null || expected == Guid.Empty || current != expected) throw new ConflictException("Запись уже изменена. Обновите данные."); }
    public static bool SameName(string left, string right) => left.Trim().ToUpperInvariant() == right.Trim().ToUpperInvariant();
    public static string Money(decimal amount) => amount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
    public static AccountDto Dto(AccountData a) => new(a.Id, a.Name, a.Currency, Money(a.OpeningBalance), Money(a.Balance), a.IsArchived, a.Version);
}
public sealed class FinancialCatalogHandler(ICurrentUser current, IFinancialBooks books)
    : IRequestHandler<ListFinancialAccounts, IReadOnlyList<AccountDto>>,
      IRequestHandler<ListFinancialCategories, IReadOnlyList<CategoryData>>,
      IRequestHandler<SaveFinancialAccount, AccountDto>, IRequestHandler<SaveFinancialCategory, CategoryData>
{
    private Guid Owner => current.UserId ?? throw new UnauthorizedAccessException();
    public async Task<IReadOnlyList<AccountDto>> HandleAsync(ListFinancialAccounts request, CancellationToken ct)
    {
        await using var book = await books.OpenAsync(Owner, false, ct);
        var result = (await book.AccountsAsync(ct)).OrderBy(x => x.IsArchived).ThenBy(x => x.Name).ThenBy(x => x.Id)
            .Select(FinancialVersions.Dto).ToArray();
        await book.CommitAsync(ct); return result;
    }
    public async Task<IReadOnlyList<CategoryData>> HandleAsync(ListFinancialCategories request, CancellationToken ct)
    {
        await using var book = await books.OpenAsync(Owner, false, ct);
        var result = (await book.CategoriesAsync(ct)).OrderBy(x => x.Name).ThenBy(x => x.Id).ToArray();
        await book.CommitAsync(ct); return result;
    }
    public async Task<AccountDto> HandleAsync(SaveFinancialAccount request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) throw new DomainException("Укажите название счёта.");
        await using var book = await books.OpenAsync(Owner, true, ct);
        var accounts = await book.AccountsAsync(ct);
        if (accounts.Any(x => x.Id != request.Id && FinancialVersions.SameName(x.Name, request.Name)))
            throw new ConflictException("Счёт с таким названием уже существует.");
        AccountData value;
        if (request.Id is { } id)
        {
            var old = await book.AccountAsync(id, ct) ?? throw new FinancialRecordNotFoundException();
            FinancialVersions.Match(old.Version, request.Version);
            var validated = FinancialRules.CreateAccount(Owner, request.Name, old.Currency, old.OpeningBalance);
            value = old with { Name = validated.Name, IsArchived = request.IsArchived, Version = Guid.NewGuid() };
        }
        else
        {
            if (accounts.Count >= 1000) throw new DomainException("Достигнут лимит счетов.");
            value = FinancialRules.CreateAccount(Owner, request.Name, book.BaseCurrency, request.OpeningBalance);
        }
        await book.SaveAccountAsync(value, ct); await book.CommitAsync(ct); return FinancialVersions.Dto(value);
    }
    public async Task<CategoryData> HandleAsync(SaveFinancialCategory request, CancellationToken ct)
    {
        await using var book = await books.OpenAsync(Owner, true, ct);
        var categories = await book.CategoriesAsync(ct);
        CategoryData value;
        if (request.Id is { } id)
        {
            var old = await book.CategoryAsync(id, ct) ?? throw new FinancialRecordNotFoundException();
            FinancialVersions.Match(old.Version, request.Version);
            var validated = Category.Create(Owner, request.Name ?? old.Name, old.Type);
            value = old with { Name = validated.Name, IsActive = request.IsActive ?? old.IsActive, Version = Guid.NewGuid() };
        }
        else
        {
            if (categories.Count >= 2000) throw new DomainException("Достигнут лимит категорий.");
            var validated = Category.Create(Owner, request.Name!, request.Type ?? throw new DomainException("Выберите тип категории."));
            value = new(validated.Id, validated.Name, validated.Type, true, Guid.NewGuid());
        }
        if (categories.Any(x => x.Id != value.Id && x.Type == value.Type && FinancialVersions.SameName(x.Name, value.Name)))
            throw new ConflictException("Категория с таким названием и типом уже существует.");
        await book.SaveCategoryAsync(value, ct); await book.CommitAsync(ct); return value;
    }
}
