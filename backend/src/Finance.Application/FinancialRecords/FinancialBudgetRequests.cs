using Finance.Application.Abstractions;
using Finance.Application.Budgets;
using Finance.Application.Messaging;
using Finance.Application.Common;
using Finance.Domain.Budgets;
using Finance.Domain.Categories;
using Finance.Domain.Common;
using Finance.Domain.Ledger;
using Finance.Domain.Transactions;

namespace Finance.Application.FinancialRecords;

public sealed record ListFinancialBudgets(DateOnly Month) : IRequest<IReadOnlyList<BudgetDto>>, IAuthenticatedRequest;
public sealed record SaveFinancialBudget(Guid? Id, Guid CategoryId, DateOnly Month, decimal Limit, Guid? Version)
    : IRequest<BudgetData>, IAuthenticatedRequest;
public sealed record DeleteFinancialBudget(Guid Id, Guid Version) : IRequest<bool>, IAuthenticatedRequest;

public sealed class FinancialBudgetHandler(ICurrentUser current, IFinancialBooks books)
    : IRequestHandler<ListFinancialBudgets, IReadOnlyList<BudgetDto>>, IRequestHandler<SaveFinancialBudget, BudgetData>,
      IRequestHandler<DeleteFinancialBudget, bool>
{
    private Guid Owner => current.UserId ?? throw new UnauthorizedAccessException();
    public async Task<IReadOnlyList<BudgetDto>> HandleAsync(ListFinancialBudgets request, CancellationToken ct)
    {
        MonthlyBudget.ValidateMonth(request.Month);
        await using var book = await books.OpenAsync(Owner, false, ct);
        var budgets = await book.BudgetsAsync(request.Month, ct);
        var categories = (await book.CategoriesAsync(ct)).ToDictionary(x => x.Id);
        var daily = await book.DailyTotalsAsync(request.Month, request.Month.AddMonths(1).AddDays(-1), ct);
        var spent = daily.Where(x => x.Type == TransactionType.Expense).GroupBy(x => x.CategoryId).ToDictionary(x => x.Key, x => x.Sum(a => a.Amount));
        var result = budgets.Select(x => {
            var category = categories[x.CategoryId]; var amount = spent.GetValueOrDefault(x.CategoryId);
            return new BudgetDto(x.Id, x.CategoryId, category.Name, category.IsActive, x.Month, FinancialVersions.Money(x.Limit),
                FinancialVersions.Money(amount), FinancialVersions.Money(Math.Max(0, x.Limit - amount)),
                FinancialVersions.Money(Math.Max(0, amount - x.Limit)), x.Version);
        }).OrderBy(x => x.CategoryName).ThenBy(x => x.Id).ToArray();
        await book.CommitAsync(ct); return result;
    }
    public async Task<BudgetData> HandleAsync(SaveFinancialBudget request, CancellationToken ct)
    {
        await using var book = await books.OpenAsync(Owner, true, ct);
        BudgetData value;
        if (request.Id is { } id)
        {
            var old = await book.BudgetAsync(id, ct) ?? throw new FinancialRecordNotFoundException();
            FinancialVersions.Match(old.Version, request.Version);
            var category = await book.CategoryAsync(old.CategoryId, ct) ?? throw new FinancialRecordNotFoundException();
            _ = MonthlyBudget.Create(Owner, Category.Create(Owner, category.Name, category.Type), old.Month, request.Limit);
            value = old with { Limit = request.Limit, Version = Guid.NewGuid() };
        }
        else
        {
            var category = await book.CategoryAsync(request.CategoryId, ct) ?? throw new FinancialRecordNotFoundException();
            if (!category.IsActive) throw new DomainException("Выберите активную расходную категорию.");
            var validated = MonthlyBudget.Create(Owner, Category.Create(Owner, category.Name, category.Type), request.Month, request.Limit);
            if ((await book.BudgetsAsync(request.Month, ct)).Any(x => x.CategoryId == category.Id))
                throw new ConflictException("Лимит для этой категории и месяца уже существует.");
            value = new(validated.Id, category.Id, request.Month, request.Limit, validated.Version);
        }
        await book.SaveBudgetAsync(value, ct); await book.CommitAsync(ct); return value;
    }
    public async Task<bool> HandleAsync(DeleteFinancialBudget request, CancellationToken ct)
    {
        await using var book = await books.OpenAsync(Owner, true, ct);
        var value = await book.BudgetAsync(request.Id, ct) ?? throw new FinancialRecordNotFoundException();
        FinancialVersions.Match(value.Version, request.Version);
        await book.DeleteBudgetAsync(value, ct); await book.CommitAsync(ct); return true;
    }
}
