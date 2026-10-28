using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Finance.Application.Abstractions;
using Finance.Application.Common;
using Finance.Application.Messaging;
using Finance.Application.Transactions;
using Finance.Application.Transactions.GetTransactions;
using Finance.Domain.Ledger;
using Finance.Domain.Transactions;

namespace Finance.Application.FinancialRecords;

public sealed record SaveFinancialTransaction(Guid? Id, Guid AccountId, Guid CategoryId, TransactionType Type,
    decimal Amount, string? Description, DateOnly? OccurredOn, Guid RequestId, Guid? Version)
    : IRequest<TransactionDto>, IAuthenticatedRequest;
public sealed record DeleteFinancialTransaction(Guid Id, Guid Version) : IRequest<bool>, IAuthenticatedRequest;
public sealed record ListFinancialTransactions(TransactionQuery Query) : IRequest<TransactionPage>, IAuthenticatedRequest;

public sealed class SaveFinancialTransactionHandler(ICurrentUser current, IFinancialBooks books, IClock clock)
    : IRequestHandler<SaveFinancialTransaction, TransactionDto>
{
    public async Task<TransactionDto> HandleAsync(SaveFinancialTransaction request, CancellationToken ct)
    {
        var owner = current.UserId ?? throw new UnauthorizedAccessException();
        if (request.Id == Guid.Empty || request.AccountId == Guid.Empty || request.CategoryId == Guid.Empty ||
            request.Id is null && request.RequestId == Guid.Empty) throw new Finance.Domain.Common.DomainException("Нужны идентификаторы счёта, категории и запроса.");
        await using var book = await books.OpenAsync(owner, true, ct);
        var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new {
            request.AccountId, request.CategoryId, request.Type,
            Amount = request.Amount.ToString("F2", CultureInfo.InvariantCulture),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(), request.OccurredOn
        })));
        if (request.Id is null && await book.ReceiptAsync(request.RequestId, ct) is { } receipt)
        {
            if (receipt.Fingerprint != fingerprint) throw new ConflictException("Запрос уже использован для другой операции.");
            var saved = await book.TransactionAsync(receipt.TransactionId, ct)
                ?? throw new ConflictException("Операция уже удалена. Повтор запроса её не восстановит.");
            var savedCategory = await book.CategoryAsync(saved.CategoryId, ct) ?? throw new InvalidOperationException("Category is missing.");
            await book.CommitAsync(ct);
            return Dto(saved, savedCategory.Name);
        }
        var previous = request.Id is { } id ? await book.TransactionAsync(id, ct)
            ?? throw new FinancialRecordNotFoundException() : null;
        if (previous is not null && (request.Version is null || request.Version != previous.Version))
            throw new ConflictException("Операция уже изменена. Обновите историю.");
        var account = await book.AccountAsync(request.AccountId, ct) ?? throw new FinancialRecordNotFoundException();
        var category = await book.CategoryAsync(request.CategoryId, ct) ?? throw new FinancialRecordNotFoundException();
        var entry = FinancialRules.Create(owner, account, category, request.Type, request.Amount, request.Description,
            request.OccurredOn ?? MoscowDate.FromUtc(clock.UtcNow), clock.UtcNow, previous);
        await book.SaveTransactionAsync(entry, previous, previous is null ? new(request.RequestId, entry.Id, fingerprint) : null, ct);
        await book.CommitAsync(ct);
        return Dto(entry, category.Name);
    }
    internal static TransactionDto Dto(TransactionData value, string category) => new(value.Id, value.CategoryId, category,
        value.Type, value.Amount.ToString("F2", CultureInfo.InvariantCulture), value.Description, value.OccurredOn,
        value.CreatedAtUtc, value.Version, value.AccountId);
}
public sealed class DeleteFinancialTransactionHandler(ICurrentUser current, IFinancialBooks books)
    : IRequestHandler<DeleteFinancialTransaction, bool>
{
    public async Task<bool> HandleAsync(DeleteFinancialTransaction request, CancellationToken ct)
    {
        await using var book = await books.OpenAsync(current.UserId ?? throw new UnauthorizedAccessException(), true, ct);
        var item = await book.TransactionAsync(request.Id, ct) ?? throw new FinancialRecordNotFoundException();
        if (item.Version != request.Version) throw new ConflictException("Операция уже изменена. Обновите историю.");
        await book.DeleteTransactionAsync(item, ct);
        await book.CommitAsync(ct);
        return true;
    }
}
public sealed class ListFinancialTransactionsHandler(ICurrentUser current, IFinancialBooks books)
    : IRequestHandler<ListFinancialTransactions, TransactionPage>
{
    public async Task<TransactionPage> HandleAsync(ListFinancialTransactions request, CancellationToken ct)
    {
        request.Query.Validate();
        await using var book = await books.OpenAsync(current.UserId ?? throw new UnauthorizedAccessException(), false, ct);
        var (items, count) = await book.TransactionsAsync(request.Query, ct);
        var categories = (await book.CategoriesAsync(ct)).ToDictionary(x => x.Id);
        var result = new TransactionPage(items.Select(x => SaveFinancialTransactionHandler.Dto(x, categories[x.CategoryId].Name)).ToArray(),
            count, request.Query.Page, request.Query.PageSize);
        await book.CommitAsync(ct);
        return result;
    }
}
public sealed class FinancialRecordNotFoundException : Exception
{
    public FinancialRecordNotFoundException() : base("Финансовая запись не найдена.") { }
}
