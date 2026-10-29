using System.Security.Cryptography;
using System.Text.Json;
using Finance.Application.Common;
using Finance.Application.FinancialRecords;
using Finance.Domain.Ledger;
using Finance.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace Finance.Infrastructure.Persistence;

internal sealed partial class FinancialBook
{
    public async Task<EncryptedFinancialBackup> BackupAsync(CancellationToken ct)
    {
        var query = db.FinancialRecords.AsNoTracking().Where(x => x.UserId == Owner && x.Kind != FinancialRecordKind.DailyTotal);
        var count = await query.CountAsync(ct);
        var bytes = await query.SumAsync(x => (long?)x.Ciphertext.Length, ct) ?? 0;
        if (count > FinancialSnapshot.MaxBackupRecords || count * 600L + bytes * 2 > FinancialRecordProtection.MaxBackupBytes)
            throw new Finance.Domain.Common.DomainException("История превышает лимит одной резервной копии (16 МБ).");
        var snapshot = FinancialSnapshot.FromRecords(BaseCurrency, await query.ToListAsync(ct), protection);
        snapshot.Validate(Owner, ct);
        var plain = JsonSerializer.SerializeToUtf8Bytes(snapshot, FinancialSnapshot.Json);
        try { return protection.EncryptBackup(workspace.VaultId, plain); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    private FinancialSnapshot DecodeBackup(JsonElement backup, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        byte[]? plain = null;
        try
        {
            FinancialSnapshot snapshot;
            if (backup.GetProperty("format").GetString() == "FinanceTrackerRecordsBackup")
            {
                var file = backup.Deserialize<EncryptedFinancialBackup>(FinancialSnapshot.Json) ?? throw FinancialSnapshot.Invalid();
                plain = protection.DecryptBackup(workspace.VaultId, file);
                snapshot = JsonSerializer.Deserialize<FinancialSnapshot>(plain, FinancialSnapshot.Json) ?? throw FinancialSnapshot.Invalid();
            }
            else
            {
                if (backup.GetProperty("format").GetString() != "FinanceTrackerEncryptedBackup" || backup.GetProperty("version").GetInt32() != 1 || backup.GetProperty("userId").GetGuid() != Owner)
                    throw FinancialSnapshot.Invalid();
                var envelope = backup.TryGetProperty("pending", out var pending) && pending.ValueKind == JsonValueKind.Object ? pending : backup.GetProperty("vault");
                if (envelope.GetProperty("id").GetGuid() != workspace.VaultId || envelope.GetProperty("format").GetInt32() != 1) throw FinancialSnapshot.Invalid();
                plain = protection.DecryptLegacyBackup(workspace.VaultId, envelope.GetProperty("revision").GetInt64(), envelope.GetProperty("nonce").GetString()!, envelope.GetProperty("ciphertext").GetString()!);
                using var document = JsonDocument.Parse(plain);
                snapshot = FinancialSnapshot.FromLegacy(document.RootElement);
            }
            if (snapshot.BaseCurrency != BaseCurrency) throw FinancialSnapshot.Invalid();
            snapshot.Validate(Owner, ct); return snapshot;
        }
        catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException or NullReferenceException or OverflowException or KeyNotFoundException) { throw FinancialSnapshot.Invalid(); }
        finally { if (plain is not null) CryptographicOperations.ZeroMemory(plain); }
    }
    public FinancialBackupPreview PreviewBackup(JsonElement backup, CancellationToken ct)
    {
        var snapshot = DecodeBackup(backup, ct);
        return new(snapshot.Transactions.Length, snapshot.Accounts.Length, snapshot.Categories.Length, workspace.Revision);
    }
    public async Task RestoreBackupAsync(JsonElement backup, long expectedRevision, CancellationToken ct)
    {
        RequireWrite();
        if (workspace.Revision != expectedRevision) throw new ConflictException("История изменилась после проверки копии. Проверьте файл повторно.");
        var snapshot = DecodeBackup(backup, ct);
        // Keep receipts from later requests so delayed devices cannot recreate removed operations/imports.
        var receipts = (await Rows(FinancialRecordKind.Request).AsNoTracking().ToListAsync(ct)).Select(protection.Decrypt<RequestReceipt>).ToArray();
        var imports = (await Rows(FinancialRecordKind.Import).AsNoTracking().ToListAsync(ct)).Select(protection.Decrypt<ImportSnapshot>).ToArray();
        snapshot = snapshot with {
            Requests = snapshot.Requests.Concat(receipts).GroupBy(x => x.RequestId).Select(x => x.First()).ToArray(),
            Imports = snapshot.Imports.Concat(imports).GroupBy(x => x.Hash).Select(x => x.First()).ToArray()
        };
        var rows = snapshot.Protect(Owner, protection, DateTimeOffset.UtcNow, true, ct);
        await db.FinancialRecords.Where(x => x.UserId == Owner).ExecuteDeleteAsync(ct);
        db.FinancialRecords.AddRange(rows);
    }
}
