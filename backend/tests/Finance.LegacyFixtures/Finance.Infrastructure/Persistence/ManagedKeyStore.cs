using Finance.Application.Abstractions;
using Finance.Application.Common;
using Finance.Application.LegacyFinance;
using Finance.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace Finance.Infrastructure.Persistence;

// FinancialPrivacyMiddleware holds the same exclusive per-owner lock as vault writes.
public sealed class ManagedKeyStore(FinanceDbContext db, FinancialKeyProtection protection, IClock clock, IPrivateFinanceStore legacyStore) : IManagedKeyStore
{
    public async Task<VaultEnvelope> StartAsync(Guid userId, CancellationToken ct)
    {
        var existing = await db.PrivateFinancialVaults.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId, ct);
        if (existing is not null)
        {
            if (!existing.IsActive || !await db.ManagedFinancialKeys.AnyAsync(x => x.UserId == userId && x.VaultId == existing.Id, ct))
                throw new ConflictException("Прежняя история защищена вашим кодом. Сначала разблокируйте её.");
            return VaultEnvelope.From(existing);
        }
        var legacy = await legacyStore.ExportAsync(userId, ct);
        var code = FinancialKeyProtection.GenerateCode();
        var id = Guid.NewGuid();
        var encrypted = FinancialKeyProtection.EncryptDocument(legacy.Document, code, userId, id);
        var vault = Finance.Domain.Users.PrivateFinancialVault.Stage(userId, id, Guid.NewGuid(), encrypted.Nonce, encrypted.Ciphertext, legacy.Fingerprint, clock.UtcNow);
        FinancialKeyProtection.NormalizeAndVerify(code, vault);
        var managed = protection.Protect(userId, id, code, clock.UtcNow);
        vault.Activate();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await PrivateFinanceStore.DeleteLegacyAsync(db, userId, ct);
        await db.Users.Where(a => a.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(a => a.BaseCurrency, "RUB"), ct);
        db.PrivateFinancialVaults.Add(vault);
        db.ManagedFinancialKeys.Add(managed);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return VaultEnvelope.From(vault);
    }
    public async Task<string?> ReadAsync(Guid userId, CancellationToken ct)
    {
        var stored = await db.ManagedFinancialKeys.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId, ct);
        if (stored is null) return null;
        if (!await db.PrivateFinancialVaults.AnyAsync(v => v.UserId == userId && v.Id == stored.VaultId, ct)) throw new ConflictException("Хранилище изменилось.");
        return protection.Unprotect(stored);
    }
    public async Task EnrollAsync(Guid userId, EnrollFinancialKey input, CancellationToken ct)
    {
        if (input.ConsentVersion != 1) throw new Finance.Domain.Common.DomainException("Подтвердите новый способ доступа к финансам.");
        var vault = await db.PrivateFinancialVaults.AsNoTracking().SingleOrDefaultAsync(v => v.UserId == userId && v.Id == input.VaultId, ct)
            ?? throw new ConflictException("Хранилище недоступно.");
        if (vault.Revision != input.ExpectedRevision) throw new ConflictException("История изменилась. Повторите настройку.");
        var code = FinancialKeyProtection.NormalizeAndVerify(input.Code, vault);
        var existing = await db.ManagedFinancialKeys.SingleOrDefaultAsync(x => x.UserId == userId, ct);
        if (existing is not null)
        {
            if (existing.VaultId != input.VaultId || protection.Unprotect(existing) != code) throw new ConflictException("Настройка доступа изменилась.");
            return;
        }
        db.ManagedFinancialKeys.Add(protection.Protect(userId, vault.Id, code, clock.UtcNow));
        await db.SaveChangesAsync(ct);
    }
}
