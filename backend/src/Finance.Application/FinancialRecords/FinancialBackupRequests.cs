using Finance.Application.Abstractions;
using Finance.Application.Messaging;
using System.Text.Json;

namespace Finance.Application.FinancialRecords;

public sealed record EncryptedFinancialBackup(string Format, int Version, Guid VaultId, byte[] Nonce, byte[] Ciphertext);
public sealed record FinancialBackupPreview(int Transactions, int Accounts, int Categories, long ExpectedRevision);
public sealed record ExportFinancialBackup : IRequest<EncryptedFinancialBackup>, IAuthenticatedRequest;
public sealed record PreviewFinancialBackup(JsonElement Backup) : IRequest<FinancialBackupPreview>, IAuthenticatedRequest;
public sealed record RestoreFinancialBackup(JsonElement Backup, long ExpectedRevision) : IRequest<bool>, IAuthenticatedRequest;
public sealed class FinancialBackupHandler(ICurrentUser current, IFinancialBooks books)
    : IRequestHandler<ExportFinancialBackup, EncryptedFinancialBackup>, IRequestHandler<PreviewFinancialBackup, FinancialBackupPreview>, IRequestHandler<RestoreFinancialBackup, bool>
{
    private Guid Owner => current.UserId ?? throw new UnauthorizedAccessException();
    public async Task<EncryptedFinancialBackup> HandleAsync(ExportFinancialBackup request, CancellationToken ct)
    {
        await using var book = await books.OpenAsync(Owner, false, ct);
        var result = await book.BackupAsync(ct); await book.CommitAsync(ct); return result;
    }
    public async Task<FinancialBackupPreview> HandleAsync(PreviewFinancialBackup request, CancellationToken ct)
    {
        await using var book = await books.OpenAsync(Owner, false, ct);
        return book.PreviewBackup(request.Backup, ct);
    }
    public async Task<bool> HandleAsync(RestoreFinancialBackup request, CancellationToken ct)
    {
        await using var book = await books.OpenAsync(Owner, true, ct);
        await book.RestoreBackupAsync(request.Backup, request.ExpectedRevision, ct);
        await book.CommitAsync(ct); return true;
    }
}
