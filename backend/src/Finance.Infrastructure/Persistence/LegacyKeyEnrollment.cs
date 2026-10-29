using Finance.Application.Common;
using Finance.Application.FinancialRecords;
using Finance.Domain.Common;
using Finance.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace Finance.Infrastructure.Persistence;

public sealed class LegacyKeyEnrollment(FinanceDbContext db, FinancialKeyProtection protection, Finance.Application.Abstractions.IClock clock) : ILegacyKeyEnrollment
{
    public async Task EnrollAsync(Guid owner, EnrollLegacyFinancialKey input, CancellationToken ct)
    {
        if (input.ConsentVersion != 1) throw new DomainException("Подтвердите новый способ доступа к финансам.");
        var vault = await db.PrivateFinancialVaults.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == owner && x.Id == input.VaultId, ct)
            ?? throw new ConflictException("Прежнее хранилище недоступно.");
        if (!vault.IsActive || vault.Revision != input.ExpectedRevision) throw new ConflictException("Прежняя история изменилась. Повторите проверку.");
        var code = FinancialKeyProtection.NormalizeAndVerify(input.Code, vault);
        var existing = await db.ManagedFinancialKeys.SingleOrDefaultAsync(x => x.UserId == owner, ct);
        if (existing is not null)
        {
            if (existing.VaultId != vault.Id || protection.Unprotect(existing) != code) throw new ConflictException("Финансовый ключ уже настроен иначе.");
            return;
        }
        db.ManagedFinancialKeys.Add(protection.Protect(owner, vault.Id, code, clock.UtcNow)); await db.SaveChangesAsync(ct);
    }
}
