namespace Finance.Api.Security;

public sealed class RefreshCookieService(IWebHostEnvironment environment, IConfiguration configuration)
{
    public const string CookieName = "finance.refresh_token";

    public string? Read(HttpRequest request) => request.Cookies[CookieName];

    public void Write(HttpResponse response, string token, DateTimeOffset expiresAtUtc)
    {
        response.Cookies.Append(CookieName, token, CreateOptions(expiresAtUtc));
    }

    public void Delete(HttpResponse response)
    {
        response.Cookies.Delete(CookieName, CreateOptions(DateTimeOffset.UnixEpoch));
    }

    private CookieOptions CreateOptions(DateTimeOffset expiresAtUtc) => new()
    {
        HttpOnly = true,
        Secure = configuration.GetValue<bool?>("Auth:SecureCookies") ?? !environment.IsDevelopment(),
        SameSite = SameSiteMode.Strict,
        Path = "/api/auth",
        Expires = expiresAtUtc,
        IsEssential = true
    };
}
