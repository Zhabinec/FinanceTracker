using System.Text.Json;
using Finance.Application.Abstractions;
using Finance.Application.Common;
using Finance.Application.FinancialRecords;
using Finance.Domain.Ledger;
using Finance.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace Finance.Infrastructure.Persistence;

public sealed class FinancialBookMigration(FinanceDbContext db, FinancialKeyProtection keys, IClock clock,
    Finance.Application.LegacyFinance.ILegacyFinancialSource legacy) : IFinancialBookMigration
{
    public async Task<FinancialBookStatus> StatusAsync(Guid owner, CancellationToken ct)
    {
        var workspace = await db.FinancialWorkspaces.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == owner, ct);
        var requiresKey = workspace is null && await db.PrivateFinancialVaults.AnyAsync(x => x.UserId == owner && x.IsActive, ct)
            && !await db.ManagedFinancialKeys.AnyAsync(x => x.UserId == owner, ct);
        return new(workspace is not null, workspace?.SourceRevision, workspace?.BaseCurrency, workspace?.Revision ?? 0, requiresKey);
    }
    public async Task<FinancialBookStatus> OpenAsync(Guid owner, CancellationToken ct)
    {
        var current = await StatusAsync(owner, ct);
        if (current.Ready) return current;
        // Bootstrap/migration still takes the owner lock and rechecks inside the transaction.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var ownerText = owner.ToString();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({ownerText}, 713))", ct);
        var existing = await StatusAsync(owner, ct);
        if (existing.Ready || existing.RequiresKey) { await transaction.CommitAsync(ct); return existing; }
        var vault = await db.PrivateFinancialVaults.SingleOrDefaultAsync(x => x.UserId == owner, ct);
        var managed = await db.ManagedFinancialKeys.SingleOrDefaultAsync(x => x.UserId == owner, ct);
        string code; Guid vaultId; long sourceRevision; FinancialSnapshot snapshot;
        if (vault is { IsActive: true })
        {
            if (managed is null || managed.VaultId != vault.Id) throw new ConflictException("Финансовый ключ недоступен.");
            code = keys.Unprotect(managed); vaultId = vault.Id; sourceRevision = vault.Revision;
            using var document = FinancialKeyProtection.DecryptDocument(code, vault);
            snapshot = FinancialSnapshot.FromLegacy(document.RootElement);
        }
        else
        {
            if (managed is not null) throw new ConflictException("Настройка финансового ключа требует проверки. Исходная история сохранена.");
            var source = await legacy.ExportAsync(owner, ct);
            if (vault is not null && vault.LegacyFingerprint != source.Fingerprint) throw new ConflictException("Незавершённый перенос отличается от исходной истории.");
            using var document = JsonDocument.Parse(source.Document);
            snapshot = FinancialSnapshot.FromLegacy(document.RootElement);
            code = FinancialKeyProtection.GenerateCode(); vaultId = Guid.NewGuid(); sourceRevision = 0;
            db.ManagedFinancialKeys.Add(keys.Protect(owner, vaultId, code, clock.UtcNow));
            if (vault is not null) db.PrivateFinancialVaults.Remove(vault);
        }
        using var protection = new FinancialRecordProtection(owner, code);
        var rows = snapshot.Protect(owner, protection, clock.UtcNow, false, ct);
        await legacy.DeleteRowsAsync(owner, ct);
        db.FinancialWorkspaces.Add(FinancialWorkspace.Create(owner, vaultId, sourceRevision, snapshot.BaseCurrency, clock.UtcNow));
        db.FinancialRecords.AddRange(rows);
        await db.SaveChangesAsync(ct);
        var persisted = await db.FinancialRecords.AsNoTracking().Where(x => x.UserId == owner).ToListAsync(ct);
        foreach (var row in persisted) { ct.ThrowIfCancellationRequested(); _ = protection.Decrypt<JsonElement>(row); }
        await transaction.CommitAsync(ct);
        return new(true, sourceRevision, snapshot.BaseCurrency);
    }
    public async Task<FinancialBookStatus> MigrateAsync(Guid owner, long expectedRevision, CancellationToken ct)
    {
        if (expectedRevision < 1) throw new Finance.Domain.Common.DomainException("Нужна текущая ревизия хранилища.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var ownerText = owner.ToString();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({ownerText}, 713))", ct);
        var existing = await StatusAsync(owner, ct);
        if (existing.Ready)
        {
            if (existing.SourceRevision != expectedRevision) throw new ConflictException("История уже перенесена из другой ревизии.");
            await transaction.CommitAsync(ct); return existing;
        }
        var vault = await db.PrivateFinancialVaults.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == owner, ct)
            ?? throw new ConflictException("Исходное хранилище не найдено.");
        if (!vault.IsActive || vault.Revision != expectedRevision) throw new ConflictException("История изменилась. Повторите перенос.");
        var managed = await db.ManagedFinancialKeys.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == owner && x.VaultId == vault.Id, ct)
            ?? throw new ConflictException("Сначала подключите серверное хранение финансового ключа.");
        var code = keys.Unprotect(managed);
        using var document = FinancialKeyProtection.DecryptDocument(code, vault);
        using var protection = new FinancialRecordProtection(owner, code);
        FinancialSnapshot snapshot;
        try { snapshot = FinancialSnapshot.FromLegacy(document.RootElement); snapshot.Validate(owner, ct); }
        catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException or NullReferenceException or OverflowException or KeyNotFoundException)
        { throw FinancialSnapshot.Invalid(); }
        var rows = snapshot.Protect(owner, protection, clock.UtcNow, false, ct);
        db.FinancialWorkspaces.Add(FinancialWorkspace.Create(owner, vault.Id, vault.Revision, snapshot.BaseCurrency, clock.UtcNow));
        db.FinancialRecords.AddRange(rows);
        await db.SaveChangesAsync(ct);
        var persisted = await db.FinancialRecords.AsNoTracking().Where(x => x.UserId == owner).ToListAsync(ct);
        foreach (var row in persisted) { ct.ThrowIfCancellationRequested(); _ = protection.Decrypt<JsonElement>(row); }
        await transaction.CommitAsync(ct);
        return new(true, vault.Revision, snapshot.BaseCurrency);
    }
}
