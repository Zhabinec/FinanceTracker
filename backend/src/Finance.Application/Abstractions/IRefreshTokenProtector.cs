namespace Finance.Application.Abstractions;

public interface IRefreshTokenProtector
{
    string Generate();
    string Hash(string token);
}
