using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Finance.Application.Abstractions;
using Finance.Application.Accounts;
using Finance.Application.Common;
using Finance.Application.Transactions;
using Finance.Domain.Common;
using Finance.Domain.Transactions;

namespace Finance.Application.Imports;

public sealed record CsvMapping(int Date, int Amount, int Category, int? Description, int? Type);
public sealed record ImportCommand(string Content, string Delimiter, Guid AccountId, CsvMapping Mapping, TransactionType DefaultType);
public sealed record ImportRow(int Row, string? Date, string? Amount, string Category, string? Type, string? Description, string[] Errors);
public sealed record ImportPreview(IReadOnlyList<ImportRow> Rows, bool CanApply, bool AlreadyImported, string Currency);
public sealed record ImportResult(Guid Id, int Count);
