using System.Globalization;
using Finance.Application.Abstractions;
using Finance.Application.Transactions;
using Finance.Application.Transactions.GetTransactions;
using Finance.Domain.Transactions;

namespace Finance.Application.Dashboard;

public sealed record CategoryTotalDto(Guid CategoryId, string CategoryName, TransactionType Type, string Amount);
public sealed record DashboardDto(DateOnly From, DateOnly To, string Income, string Expense, string Result,
    IReadOnlyList<CategoryTotalDto> Categories, IReadOnlyList<TransactionDto> Recent);
