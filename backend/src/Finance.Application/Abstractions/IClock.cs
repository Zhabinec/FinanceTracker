namespace Finance.Application.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
