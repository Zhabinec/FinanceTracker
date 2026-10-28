using Finance.Application.Accounts;
using Finance.Application.Budgets;
using Finance.Application.Dashboard;
using Finance.Application.FinancialRecords;
using Finance.Application.Messaging;
using Finance.Application.Imports;
using Finance.Application.Transactions;
using Finance.Application.Transactions.GetTransactions;
using Finance.Application.Transfers;
using Finance.Domain.Ledger;
using Finance.Infrastructure.Persistence;

namespace Finance.Api;

internal static class FinancialRecordServices
{
    public static void AddTo(IServiceCollection services)
    {
        services.AddScoped(typeof(RequestDispatcher<,>));
        services.AddScoped(typeof(IRequestBehavior<,>), typeof(AuthenticatedRequestBehavior<,>));
        services.AddScoped<IFinancialBooks, FinancialBooks>();
        services.AddScoped<IFinancialBookMigration, FinancialBookMigration>();
        Register<MigrateFinancialBook, FinancialBookStatus, MigrateFinancialBookHandler>(services);
        Register<SaveFinancialTransaction, TransactionDto, SaveFinancialTransactionHandler>(services);
        Register<DeleteFinancialTransaction, bool, DeleteFinancialTransactionHandler>(services);
        Register<ListFinancialTransactions, TransactionPage, ListFinancialTransactionsHandler>(services);
        Register<ListFinancialAccounts, IReadOnlyList<AccountDto>, FinancialCatalogHandler>(services);
        Register<ListFinancialCategories, IReadOnlyList<CategoryData>, FinancialCatalogHandler>(services);
        Register<SaveFinancialAccount, AccountDto, FinancialCatalogHandler>(services);
        Register<SaveFinancialCategory, CategoryData, FinancialCatalogHandler>(services);
        Register<ListFinancialBudgets, IReadOnlyList<BudgetDto>, FinancialBudgetHandler>(services);
        Register<SaveFinancialBudget, BudgetData, FinancialBudgetHandler>(services);
        Register<DeleteFinancialBudget, bool, FinancialBudgetHandler>(services);
        Register<ReadFinancialDashboard, DashboardDto, FinancialDashboardHandler>(services);
        Register<ListFinancialTransfers, TransferPage, FinancialTransferHandler>(services);
        Register<CreateFinancialTransfer, TransferDto, FinancialTransferHandler>(services);
        Register<CancelFinancialTransfer, bool, FinancialTransferHandler>(services);
        Register<PreviewFinancialCsv, ImportPreview, FinancialImportHandler>(services);
        Register<ApplyFinancialCsv, ImportResult, FinancialImportHandler>(services);
    }
    private static void Register<TRequest, TResponse, THandler>(IServiceCollection services)
        where TRequest : class, IRequest<TResponse> where THandler : class, IRequestHandler<TRequest, TResponse> =>
        services.AddScoped<IRequestHandler<TRequest, TResponse>, THandler>();
}
