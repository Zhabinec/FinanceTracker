using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Finance.Api.Security;

public sealed record GoogleSettings(bool Enabled, string RedirectUri);
public static class GoogleAuthentication
{
    public const string Scheme = "Google";
    public const string ExternalCookie = "GoogleExternal";
    public static void AddGoogleLogin(this IServiceCollection services, IConfiguration configuration)
    {
        var id = configuration["Google:ClientId"];
        var secret = configuration["Google:ClientSecret"];
        var redirect = configuration["Google:RedirectUri"] ?? "";
        var enabled = !string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(secret);
        if (enabled && (!Uri.TryCreate(redirect, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)) ||
            uri.AbsolutePath != "/api/auth/google/callback" || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.UserInfo.Length > 0))
            throw new InvalidOperationException("Google:RedirectUri must be the public HTTPS /api/auth/google/callback URL (HTTP allowed only on localhost).");
        services.AddSingleton(new GoogleSettings(enabled, redirect));
        if (!enabled) return;
        services.AddAuthentication().AddCookie(ExternalCookie, options =>
        {
            options.Cookie.Name = "finance.google.external";
            options.Cookie.Path = "/api/auth/google";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
            options.SlidingExpiration = false;
        }).AddOpenIdConnect(Scheme, options =>
        {
            options.Authority = "https://accounts.google.com";
            options.ClientId = id!;
            options.ClientSecret = secret!;
            options.SignInScheme = ExternalCookie;
            options.CallbackPath = "/api/auth/google/callback";
            options.ResponseType = OpenIdConnectResponseType.Code;
            options.ResponseMode = OpenIdConnectResponseMode.Query;
            options.UsePkce = true;
            options.MapInboundClaims = false;
            options.SaveTokens = false;
            options.Scope.Clear(); options.Scope.Add("openid"); options.Scope.Add("email");
            options.RemoteAuthenticationTimeout = TimeSpan.FromMinutes(5);
            options.Events = new OpenIdConnectEvents
            {
                OnRedirectToIdentityProvider = context =>
                { context.ProtocolMessage.RedirectUri = redirect; context.ProtocolMessage.Prompt = "select_account"; return Task.CompletedTask; },
                OnRemoteFailure = context =>
                { context.HandleResponse(); context.Response.Redirect("/login?google=failed"); return Task.CompletedTask; }
            };
        });
    }
}
