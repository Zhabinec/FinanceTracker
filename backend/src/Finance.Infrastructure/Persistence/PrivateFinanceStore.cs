using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Finance.Application.Abstractions;
using Finance.Application.Common;
using Finance.Application.PrivateFinance;
using Finance.Application.Transactions;
using Finance.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Finance.Infrastructure.Persistence;

// All calls are serialized with financial requests by FinancialPrivacyMiddleware.
public sealed class PrivateFinanceStore(FinanceDbContext db, IClock clock) : IPrivateFinanceStore
{
    public async Task<VaultEnvelope?> ReadAsync(Guid userId, CancellationToken ct)
    {
        var vault = await db.PrivateFinancialVaults.AsNoTracking().SingleOrDefaultAsync(v => v.UserId == userId, ct);
        return vault is null ? null : VaultEnvelope.From(vault);
    }

    public async Task<LegacyFinancialExport> ExportAsync(Guid userId, CancellationToken ct)
    {
        if (await db.PrivateFinancialVaults.AnyAsync(v => v.UserId == userId && v.IsActive, ct)) throw new ConflictException("Открытая история уже перенесена.");
        await EnsureBounded(db.Accounts.Where(a => a.UserId == userId), 1000, ct);
        await EnsureBounded(db.Categories.Where(a => a.UserId == userId), 2000, ct);
        await EnsureBounded(db.Transactions.Where(a => a.UserId == userId), 20000, ct);
        await EnsureBounded(db.AccountTransfers.Where(a => a.UserId == userId), 10000, ct);
        await EnsureBounded(db.MonthlyBudgets.Where(a => a.UserId == userId), 10000, ct);
        await EnsureBounded(db.TransactionRequests.Where(a => a.UserId == userId), 30000, ct);
        await EnsureBounded(db.ImportBatches.Where(a => a.UserId == userId), 20000, ct);
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId, ct);
        var accounts = await db.Accounts.AsNoTracking().Where(a => a.UserId == userId).OrderBy(a => a.Id).ToListAsync(ct);
        var categories = await db.Categories.AsNoTracking().Where(a => a.UserId == userId).OrderBy(a => a.Id).ToListAsync(ct);
        var transactions = await db.Transactions.AsNoTracking().Where(a => a.UserId == userId).OrderBy(a => a.Id).ToListAsync(ct);
        var transfers = await db.AccountTransfers.AsNoTracking().Where(a => a.UserId == userId).OrderBy(a => a.Id).ToListAsync(ct);
        var budgets = await db.MonthlyBudgets.AsNoTracking().Where(a => a.UserId == userId).OrderBy(a => a.Id).ToListAsync(ct);
        var requests = await db.TransactionRequests.AsNoTracking().Where(a => a.UserId == userId).OrderBy(a => a.RequestId).ToListAsync(ct);
        var imports = await db.ImportBatches.AsNoTracking().Where(a => a.UserId == userId).OrderBy(a => a.Id).ToListAsync(ct);
        static string Money(decimal value) => value.ToString("F2", CultureInfo.InvariantCulture);
        static string Day(DateTimeOffset value) => MoscowDate.FromUtc(value).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var document = JsonSerializer.Serialize(new
        {
            format = 1, baseCurrency = user.BaseCurrency,
            accounts = accounts.Select(a => new { a.Id, a.Name, a.Currency, openingBalance = Money(a.OpeningBalance), a.IsArchived, a.Version }),
            categories = categories.Select(a => new { a.Id, a.Name, type = a.Type.ToString(), a.IsActive }),
            transactions = transactions.Select(a => new { a.Id, a.AccountId, a.CategoryId, type = a.Type.ToString(), amount = Money(a.Amount), a.Description, occurredOn = Day(a.OccurredAtUtc), a.CreatedAtUtc, a.Version }),
            transfers = transfers.Select(a => new { a.Id, a.RequestId, a.SourceAccountId, a.TargetAccountId, a.Currency, amount = Money(a.Amount), a.Description, occurredOn = Day(a.CreatedAtUtc), a.CreatedAtUtc, a.CancelledAtUtc, a.Version }),
            budgets = budgets.Select(a => new { a.Id, a.CategoryId, month = a.Month.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), limit = Money(a.Limit), a.Version }),
            requests = requests.Select(a => new { a.RequestId, a.TransactionId, fingerprint = "legacy" }),
            imports = imports.Select(a => a.Fingerprint)
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (Encoding.UTF8.GetByteCount(document) > 2_000_000) throw new ConflictException("История превышает 2 МБ. Нужен отдельный перенос без потери записей.");
        return new(document, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(document))));
    }

    public async Task<VaultEnvelope> StageAsync(Guid userId, StageVault input, CancellationToken ct)
    {
        ValidateFormat(input.Format);
        var existing = await db.PrivateFinancialVaults.AsNoTracking().SingleOrDefaultAsync(v => v.UserId == userId, ct);
        if (existing is not null)
        {
            if (!existing.IsActive && Same(existing, input.Id, input.MutationId, input.Nonce, input.Ciphertext)) return VaultEnvelope.From(existing);
            throw Changed();
        }
        var legacy = await ExportAsync(userId, ct);
        if (!string.Equals(legacy.Fingerprint, input.LegacyFingerprint, StringComparison.Ordinal)) throw Changed();
        var vault = PrivateFinancialVault.Stage(userId, input.Id, input.MutationId, input.Nonce, input.Ciphertext, legacy.Fingerprint, clock.UtcNow);
        db.PrivateFinancialVaults.Add(vault);
        await db.SaveChangesAsync(ct);
        return VaultEnvelope.From(vault);
    }

    public async Task<VaultEnvelope> ActivateAsync(Guid userId, Guid id, long revision, CancellationToken ct)
    {
        var vault = await Find(userId, id, ct);
        if (vault.Revision != revision) throw Changed();
        if (vault.IsActive) return VaultEnvelope.From(vault);
        var legacy = await ExportAsync(userId, ct);
        if (legacy.Fingerprint != vault.LegacyFingerprint) throw Changed();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Only after the client downloaded and verified its staged encrypted copy.
        await DeleteLegacyAsync(db, userId, ct);
        await db.Users.Where(a => a.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(a => a.BaseCurrency, "RUB"), ct);
        vault.Activate();
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return VaultEnvelope.From(vault);
    }

    public async Task<VaultEnvelope> WriteAsync(Guid userId, WriteVault input, CancellationToken ct)
    {
        ValidateFormat(input.Format);
        var vault = await Find(userId, input.Id, ct);
        if (!vault.IsActive) throw new ConflictException("Сначала завершите перенос.");
        if (Same(vault, input.Id, input.MutationId, input.Nonce, input.Ciphertext)) return VaultEnvelope.From(vault);
        if (vault.Revision != input.ExpectedRevision || vault.MutationId == input.MutationId) throw Changed();
        vault.Write(input.MutationId, input.Nonce, input.Ciphertext, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return VaultEnvelope.From(vault);
    }

    public async Task CancelStageAsync(Guid userId, Guid id, long revision, CancellationToken ct)
    {
        var vault = await Find(userId, id, ct);
        if (vault.IsActive || vault.Revision != revision) throw Changed();
        db.PrivateFinancialVaults.Remove(vault);
        await db.SaveChangesAsync(ct);
    }
    internal static async Task DeleteLegacyAsync(FinanceDbContext db, Guid userId, CancellationToken ct)
    {
        await db.AccountTransfers.Where(a => a.UserId == userId).ExecuteDeleteAsync(ct);
        await db.TransactionRequests.Where(a => a.UserId == userId).ExecuteDeleteAsync(ct);
        await db.MonthlyBudgets.Where(a => a.UserId == userId).ExecuteDeleteAsync(ct);
        await db.ImportBatches.Where(a => a.UserId == userId).ExecuteDeleteAsync(ct);
        await db.Transactions.Where(a => a.UserId == userId).ExecuteDeleteAsync(ct);
        await db.Categories.Where(a => a.UserId == userId).ExecuteDeleteAsync(ct);
        await db.Accounts.Where(a => a.UserId == userId).ExecuteDeleteAsync(ct);
    }
    private async Task<PrivateFinancialVault> Find(Guid userId, Guid id, CancellationToken ct) =>
        await db.PrivateFinancialVaults.SingleOrDefaultAsync(v => v.UserId == userId && v.Id == id, ct) ?? throw Changed();
    private static async Task EnsureBounded<T>(IQueryable<T> query, int limit, CancellationToken ct)
    { if (await query.Take(limit + 1).CountAsync(ct) > limit) throw new ConflictException("История превышает лимиты переноса. Исходные данные сохранены; нужен отдельный перенос без усечения."); }
    private static bool Same(PrivateFinancialVault vault, Guid id, Guid mutationId, string nonce, string ciphertext) =>
        vault.Id == id && vault.MutationId == mutationId && vault.Nonce == nonce && vault.Ciphertext == ciphertext;
    private static void ValidateFormat(int format) { if (format != 1) throw new Finance.Domain.Common.DomainException("Неподдерживаемый формат хранилища."); }
    private static ConflictException Changed() => new("Хранилище изменилось на другом устройстве. Обновите данные и повторите действие.");
}
