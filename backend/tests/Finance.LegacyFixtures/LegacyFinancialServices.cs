using Finance.Application.Abstractions;
using Finance.Application.Accounts;
using Finance.Application.Budgets;
using Finance.Application.Categories.Create;
using Finance.Application.Categories.Deactivate;
using Finance.Application.Categories.GetCategories;
using Finance.Application.Categories.Rename;
using Finance.Application.Categories.Restore;
using Finance.Application.Transactions.Create;
using Finance.Application.Transactions.Delete;
using Finance.Application.Transactions.GetTransactions;
using Finance.Application.Transactions.Update;
using Finance.Application.Transfers;
using Finance.Application.Imports;
using Finance.Application.Dashboard;
using Finance.Application.LegacyFinance;
using Finance.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Finance.LegacyFixtures;

// Test-only historical writers generate migration fixtures and exercise the original protocol.
// This assembly is never referenced or published by Finance.Api.
public static class LegacyFinancialServices
{
    public static void AddLegacyFinancialFixtures(this IServiceCollection services)
    {
        services.AddControllers().AddApplicationPart(typeof(Finance.Api.Controllers.TransactionsController).Assembly);
        services.AddScoped<ICategoryStore, CategoryStore>(); services.AddScoped<ITransactionStore, TransactionStore>();
        services.AddScoped<IAccountStore, AccountStore>(); services.AddScoped<AccountHandler>();
        services.AddScoped<ITransferStore, TransferStore>(); services.AddScoped<TransferHandler>();
        services.AddScoped<IBudgetStore, BudgetStore>(); services.AddScoped<BudgetHandler>();
        services.AddScoped<IImportStore, ImportStore>(); services.AddScoped<ImportHandler>();
        services.AddScoped<IDashboardStore, DashboardStore>(); services.AddScoped<DashboardHandler>();
        services.AddScoped<IPrivateFinanceStore, PrivateFinanceStore>(); services.AddScoped<PrivateFinanceHandler>();
        services.AddScoped<IManagedKeyStore, ManagedKeyStore>(); services.AddScoped<ManagedKeyHandler>();
        services.AddScoped<CreateCategoryHandler>(); services.AddScoped<GetCategoriesHandler>(); services.AddScoped<RenameCategoryHandler>();
        services.AddScoped<DeactivateCategoryHandler>(); services.AddScoped<RestoreCategoryHandler>();
        services.AddScoped<CreateTransactionHandler>(); services.AddScoped<GetTransactionsHandler>(); services.AddScoped<UpdateTransactionHandler>(); services.AddScoped<DeleteTransactionHandler>();
    }
}
