using Finance.Domain.Transactions;
using Finance.Application.Transactions.GetTransactions;
using FinanceTransaction = Finance.Domain.Transactions.Transaction;

namespace Finance.Application.Abstractions;

public sealed record TransactionRecord(
    FinanceTransaction Transaction,
    string CategoryName);

