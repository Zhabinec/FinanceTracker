using System.Data.Common;
using Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Finance.Api.Security;

public sealed class FinancialPrivacyMiddleware(RequestDelegate next)
{
    private static readonly string[] FinancialPaths = ["/api/accounts", "/api/categories", "/api/transactions", "/api/transfers", "/api/budgets", "/api/dashboard", "/api/imports"];
    public async Task InvokeAsync(HttpContext context, FinanceDbContext db)
    {
        if (context.GetEndpoint() is null) { await next(context); return; }
        var legacy = FinancialPaths.Any(path => context.Request.Path.StartsWithSegments(path));
        var vault = context.Request.Path.StartsWithSegments("/api/private-finance") || context.Request.Path.StartsWithSegments("/api/finance/legacy");
        if ((!legacy && !vault) || !Guid.TryParse(context.User.FindFirst("sub")?.Value, out var userId))
        { await next(context); return; }
        // Hold the same physical connection through store transactions. Direct PostgreSQL
        // is required: transaction-mode PgBouncer cannot preserve a session advisory lock.
        var shared = legacy && (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method));
        await db.Database.OpenConnectionAsync(context.RequestAborted);
        var connection = db.Database.GetDbConnection();
        var locked = false;
        try
        {
            await using (var command = LockCommand(connection, userId, shared, false))
            { await command.ExecuteScalarAsync(context.RequestAborted); locked = true; }
            if (await db.FinancialWorkspaces.AnyAsync(w => w.UserId == userId, context.RequestAborted))
            {
                context.Response.StatusCode = StatusCodes.Status409Conflict;
                await context.Response.WriteAsJsonAsync(new Microsoft.AspNetCore.Mvc.ProblemDetails
                { Status = 409, Title = "История перенесена в серверное хранилище. Обновите приложение." }, context.RequestAborted);
                return;
            }
            if (legacy && await db.PrivateFinancialVaults.AnyAsync(v => v.UserId == userId, context.RequestAborted))
            {
                context.Response.StatusCode = StatusCodes.Status409Conflict;
                await context.Response.WriteAsJsonAsync(new Microsoft.AspNetCore.Mvc.ProblemDetails
                { Status = 409, Title = "Финансы этого аккаунта доступны только через приватное хранилище." }, context.RequestAborted);
                return;
            }
            await next(context);
        }
        finally
        {
            try
            {
                if (locked)
                {
                    await using var unlock = LockCommand(connection, userId, shared, true);
                    await unlock.ExecuteScalarAsync(CancellationToken.None);
                }
            }
            finally { await db.Database.CloseConnectionAsync(); }
        }
    }
    private static DbCommand LockCommand(DbConnection connection, Guid userId, bool shared, bool unlock)
    {
        var command = connection.CreateCommand();
        var function = unlock ? "pg_advisory_unlock" : "pg_advisory_lock";
        if (shared) function += "_shared";
        command.CommandText = $"SELECT {function}(hashtextextended(@owner, 713));";
        command.CommandTimeout = 15;
        var parameter = command.CreateParameter(); parameter.ParameterName = "owner"; parameter.Value = userId.ToString();
        command.Parameters.Add(parameter);
        return command;
    }
}
