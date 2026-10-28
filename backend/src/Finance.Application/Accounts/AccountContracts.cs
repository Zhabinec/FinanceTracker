using System.Globalization;
using Finance.Application.Abstractions;
using Finance.Application.Common;
using Finance.Domain.Accounts;

namespace Finance.Application.Accounts;

public sealed record AccountDto(Guid Id, string Name, string Currency, string OpeningBalance, string Balance, bool IsArchived, Guid Version);
