using System.Security.Cryptography;
using System.Text;
using Finance.Application.Abstractions;

namespace Finance.Infrastructure.Security;

public sealed class RefreshTokenProtector : IRefreshTokenProtector
{
    public string Generate() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

    public string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
