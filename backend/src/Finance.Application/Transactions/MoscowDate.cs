namespace Finance.Application.Transactions;

internal static class MoscowDate
{
    private static readonly TimeSpan UtcOffset = TimeSpan.FromHours(3);

    public static DateTimeOffset ToUtc(DateOnly date)
    {
        var localMidnight = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(localMidnight, UtcOffset).ToUniversalTime();
    }

    public static DateOnly FromUtc(DateTimeOffset value) =>
        DateOnly.FromDateTime(value.ToOffset(UtcOffset).DateTime);
}
