using Finance.Application.Abstractions;
using Finance.Application.Dashboard;
using Finance.Application.Messaging;
using Finance.Application.Transactions;
using Finance.Application.Transactions.GetTransactions;
using Finance.Domain.Transactions;

namespace Finance.Application.FinancialRecords;

public sealed record ReadFinancialDashboard(DateOnly? From, DateOnly? To) : IRequest<DashboardDto>, IAuthenticatedRequest;
public sealed class FinancialDashboardHandler(ICurrentUser current, IFinancialBooks books, IClock clock)
    : IRequestHandler<ReadFinancialDashboard, DashboardDto>
{
    public async Task<DashboardDto> HandleAsync(ReadFinancialDashboard request, CancellationToken ct)
    {
        var today = MoscowDate.FromUtc(clock.UtcNow);
        var from = request.From ?? new DateOnly(today.Year, today.Month, 1); var to = request.To ?? today;
        new TransactionQuery(From: from, To: to).Validate();
        await using var book = await books.OpenAsync(current.UserId ?? throw new UnauthorizedAccessException(), false, ct);
        var categories = (await book.CategoriesAsync(ct)).ToDictionary(x => x.Id);
        var totals = (await book.DailyTotalsAsync(from, to, ct)).GroupBy(x => new { x.CategoryId, x.Type })
            .Select(x => new { x.Key.CategoryId, x.Key.Type, Amount = x.Sum(a => a.Amount) }).ToArray();
        var income = totals.Where(x => x.Type == TransactionType.Income).Sum(x => x.Amount);
        var expense = totals.Where(x => x.Type == TransactionType.Expense).Sum(x => x.Amount);
        var (recent, _) = await book.TransactionsAsync(new(From: from, To: to, PageSize: 5), ct);
        var result = new DashboardDto(from, to, FinancialVersions.Money(income), FinancialVersions.Money(expense), FinancialVersions.Money(income - expense),
            totals.OrderByDescending(x => x.Amount).ThenBy(x => x.CategoryId).Select(x => new CategoryTotalDto(x.CategoryId,
                categories[x.CategoryId].Name, x.Type, FinancialVersions.Money(x.Amount))).ToArray(),
            recent.Select(x => SaveFinancialTransactionHandler.Dto(x, categories[x.CategoryId].Name)).ToArray());
        await book.CommitAsync(ct); return result;
    }
}
