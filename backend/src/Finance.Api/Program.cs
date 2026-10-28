using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Text.Json.Serialization;
using Finance.Api.HealthChecks;
using Finance.Api.Hosting;
using Finance.Api.Security;
using Finance.Application.Abstractions;
using Finance.Application.Auth.Login;
using Finance.Application.Auth.Recovery;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Finance.Application.Auth.Register;
using Finance.Application.Auth.Refresh;
using Finance.Application.Auth.Logout;
using Finance.Application.Auth.Telegram;
using Finance.Application.Users.GetCurrent;
using Finance.Application.Categories.Create;
using Finance.Application.Categories.Deactivate;
using Finance.Application.Categories.GetCategories;
using Finance.Application.Categories.Rename;
using Finance.Application.Categories.Restore;
using Finance.Application.Transactions.Create;
using Finance.Application.Transactions.Delete;
using Finance.Application.Transactions.GetTransactions;
using Finance.Application.Transactions.Update;
using Finance.Infrastructure.Persistence;
using Finance.Infrastructure.Time;
using Finance.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 65_536;
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(15);
    options.Limits.MaxRequestHeadersTotalSize = 16_384;
});

var databaseConnectionString = builder.Configuration.GetConnectionString("Database")
    ?? throw new InvalidOperationException(
        "Connection string 'Database' is not configured.");
var jwtOptions = builder.Configuration
    .GetSection(JwtOptions.SectionName)
    .Get<JwtOptions>()
    ?? throw new InvalidOperationException("JWT configuration is missing.");
var refreshTokenLifetimeDays = builder.Configuration.GetValue<int>("Auth:RefreshTokenLifetimeDays");

if (string.IsNullOrWhiteSpace(jwtOptions.Issuer) ||
    string.IsNullOrWhiteSpace(jwtOptions.Audience) ||
    Encoding.UTF8.GetByteCount(jwtOptions.SigningKey) < 32 ||
    jwtOptions.AccessTokenLifetimeMinutes is <= 0 or > 60)
{
    throw new InvalidOperationException(
        "JWT issuer, audience, signing key of at least 32 bytes, and lifetime from 1 to 60 minutes are required.");
}
if (refreshTokenLifetimeDays is <= 0 or > 90)
    throw new InvalidOperationException("Refresh token lifetime must be from 1 to 90 days.");

var jwtSigningKey = new SymmetricSecurityKey(
    Encoding.UTF8.GetBytes(jwtOptions.SigningKey));
var databaseOptions = new Npgsql.NpgsqlConnectionStringBuilder(databaseConnectionString);
if (databaseOptions.Multiplexing || databaseOptions.Host?.Contains("-pooler.", StringComparison.OrdinalIgnoreCase) == true)
    throw new InvalidOperationException("Use the direct Neon connection, not the transaction pooler, for private financial migration locks.");

builder.Services.AddDbContext<FinanceDbContext>(options =>
    options.UseNpgsql(databaseConnectionString));
builder.Services.AddScoped<IUserRegistrationStore, UserRegistrationStore>();
builder.Services.AddScoped<Finance.Application.PrivateFinance.IPrivateFinanceStore, PrivateFinanceStore>();
builder.Services.AddScoped<Finance.Application.PrivateFinance.PrivateFinanceHandler>();
builder.Services.AddScoped<IUserLoginStore, UserLoginStore>();
builder.Services.AddScoped<IUserProfileStore, UserProfileStore>();
builder.Services.AddScoped<ICategoryStore, CategoryStore>();
builder.Services.AddScoped<ITransactionStore, TransactionStore>();
builder.Services.AddScoped<Finance.Application.Accounts.IAccountStore, AccountStore>();
builder.Services.AddScoped<Finance.Application.Accounts.AccountHandler>();
builder.Services.AddScoped<Finance.Application.Transfers.ITransferStore, TransferStore>();
builder.Services.AddScoped<Finance.Application.Transfers.TransferHandler>();
builder.Services.AddScoped<Finance.Application.Budgets.IBudgetStore, BudgetStore>();
builder.Services.AddScoped<Finance.Application.Budgets.BudgetHandler>();
builder.Services.AddScoped<Finance.Application.Imports.IImportStore, ImportStore>();
builder.Services.AddScoped<Finance.Application.Imports.ImportHandler>();
builder.Services.AddHttpClient("cbr-rates", client => { client.Timeout = TimeSpan.FromSeconds(10); client.MaxResponseContentBufferSize = 256_000; });
builder.Services.AddSingleton<Finance.Application.Currencies.IExchangeRateProvider>(services =>
    new Finance.Infrastructure.Currencies.CbrExchangeRateProvider(
        () => services.GetRequiredService<IHttpClientFactory>().CreateClient("cbr-rates"),
        services.GetRequiredService<IClock>(), services.GetRequiredService<ILogger<Finance.Infrastructure.Currencies.CbrExchangeRateProvider>>()));
builder.Services.AddScoped<Finance.Application.Dashboard.IDashboardStore, DashboardStore>();
builder.Services.AddScoped<Finance.Application.Dashboard.DashboardHandler>();
builder.Services.AddScoped<IRefreshSessionStore, RefreshSessionStore>();
builder.Services.AddSingleton(new TelegramSettings(builder.Configuration["Telegram:BotToken"] ?? "", builder.Configuration["Telegram:BotUsername"] ?? "PetProjectFinanceBot"));
builder.Services.AddSingleton<ITelegramInitDataVerifier, TelegramInitDataVerifier>();
builder.Services.AddScoped<ITelegramAuthStore, TelegramAuthStore>();
builder.Services.AddScoped<TelegramAuthHandler>();
builder.Services.AddScoped<Finance.Application.Auth.Google.IGoogleAuthStore, GoogleAuthStore>();
builder.Services.AddScoped<Finance.Application.Auth.Google.GoogleAuthHandler>();
builder.Services.AddGoogleLogin(builder.Configuration);
builder.Services.AddScoped<IPasswordRecoveryStore, PasswordRecoveryStore>();
builder.Services.AddScoped<PasswordRecoveryHandler>();
builder.Services.AddScoped<Finance.Application.Auth.Verification.IEmailVerification, Finance.Application.Auth.Verification.EmailVerificationHandler>();
builder.Services.AddScoped<Finance.Application.Auth.Verification.IEmailVerificationStore, EmailVerificationStore>();
var recoveryUrl = builder.Configuration.GetValue<bool>("Hosting:UseRenderExternalUrl")
    ? builder.Configuration["RENDER_EXTERNAL_URL"] ?? throw new InvalidOperationException("Render public URL is missing.")
    : builder.Configuration["Auth:FrontendUrl"] ?? "http://localhost:5173";
if (!Uri.TryCreate(recoveryUrl, UriKind.Absolute, out var frontendUri) || frontendUri.Scheme is not ("https" or "http"))
    throw new InvalidOperationException("Auth:FrontendUrl must be an absolute HTTP(S) URL.");
builder.Services.AddSingleton(new PasswordRecoverySettings(recoveryUrl));
builder.Services.AddSingleton(new RecoveryEmailOptions(
    builder.Configuration["Email:Provider"] ?? "Mailpit",
    builder.Configuration["Email:Endpoint"] ?? "http://localhost:8025/api/v1/send",
    builder.Configuration["Email:From"] ?? "finance@example.test", builder.Configuration["Email:ApiKey"]));
builder.Services.AddHttpClient<RecoveryEmailSender>(http => http.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddScoped<IRecoveryEmailSender>(services => services.GetRequiredService<RecoveryEmailSender>());
builder.Services.AddScoped<Finance.Application.Auth.Verification.IConfirmationEmailSender>(services => services.GetRequiredService<RecoveryEmailSender>());
builder.Services.AddRateLimiter(options =>
{
    // Shared process budgets run before authentication and its database lookup.
    // Do not partition by client-supplied forwarded headers or an unverified JWT subject.
    options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
        PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            if (!context.Request.Path.StartsWithSegments("/api") && context.Request.Path != "/health")
                return RateLimitPartition.GetNoLimiter("static");
            var health = context.Request.Path == "/health";
            return RateLimitPartition.GetFixedWindowLimiter(health ? "health" : "api", _ => new FixedWindowRateLimiterOptions
            { PermitLimit = health ? 60 : builder.Configuration.GetValue("Http:ApiPermitLimit", 600), Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });
        }),
        PartitionedRateLimiter.Create<HttpContext, string>(context => context.Request.Path.StartsWithSegments("/api")
            ? RateLimitPartition.GetConcurrencyLimiter("api", _ => new ConcurrencyLimiterOptions
            { PermitLimit = builder.Configuration.GetValue("Http:ApiConcurrencyLimit", 8), QueueLimit = 0 })
            : RateLimitPartition.GetNoLimiter("static")),
        PartitionedRateLimiter.Create<HttpContext, string>(context =>
            HttpMethods.IsPost(context.Request.Method) &&
                (context.Request.Path.Value?.TrimEnd('/').Equals("/api/auth/register", StringComparison.OrdinalIgnoreCase) == true ||
                 context.Request.Path.Value?.TrimEnd('/').Equals("/api/auth/telegram/register", StringComparison.OrdinalIgnoreCase) == true)
            ? RateLimitPartition.GetFixedWindowLimiter("registration", _ => new FixedWindowRateLimiterOptions
            { PermitLimit = builder.Configuration.GetValue("Auth:RegistrationPermitLimit", 20), Window = TimeSpan.FromMinutes(15), QueueLimit = 0 })
            : RateLimitPartition.GetNoLimiter("other")));
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        var seconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
            ? Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)) : 2;
        context.HttpContext.Response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var problem = new Microsoft.AspNetCore.Mvc.ProblemDetails
        {
            Status = 429,
            Title = $"Слишком много попыток. Повторите через {Math.Max(1, (int)Math.Ceiling(seconds / 60d))} мин."
        };
        problem.Extensions["retryAfterSeconds"] = seconds;
        problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
        await context.HttpContext.Response.WriteAsJsonAsync(problem, options: (System.Text.Json.JsonSerializerOptions?)null,
            contentType: "application/problem+json", cancellationToken: cancellationToken);
    };
    options.AddPolicy("password-recovery", context => RateLimitPartition.GetFixedWindowLimiter(
        $"{context.Request.Path.Value?.TrimEnd('/').ToLowerInvariant()}:{context.Connection.RemoteIpAddress}", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = builder.Configuration.GetValue("Auth:RecoveryPermitLimit", 10), Window = TimeSpan.FromMinutes(15), QueueLimit = 0 }));
    options.AddPolicy("telegram-auth", context => RateLimitPartition.GetFixedWindowLimiter(
        $"{context.Request.Path}:{context.Connection.RemoteIpAddress}", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = builder.Configuration.GetValue("Telegram:AuthPermitLimit", 30), Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("password-login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = builder.Configuration.GetValue("Auth:LoginPermitLimit", 15), Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("google-auth", context => RateLimitPartition.GetFixedWindowLimiter(
        $"google:{context.Connection.RemoteIpAddress}", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.AddSingleton<IPasswordHasher, AspNetCorePasswordHasher>();
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddSingleton(jwtOptions);
builder.Services.AddSingleton<IAccessTokenProvider, JwtAccessTokenProvider>();
builder.Services.AddSingleton<ITelegramAccessTokenProvider, JwtAccessTokenProvider>();
builder.Services.AddSingleton<IRefreshTokenProtector, RefreshTokenProtector>();
builder.Services.AddSingleton(new RefreshTokenSettings(refreshTokenLifetimeDays));
builder.Services.AddScoped<RefreshCookieService>();
builder.Services.AddScoped<RegisterUserHandler>();
builder.Services.AddScoped<LoginUserHandler>();
builder.Services.AddScoped<GetCurrentUserHandler>();
builder.Services.AddScoped<RefreshTokensHandler>();
builder.Services.AddScoped<LogoutHandler>();
builder.Services.AddScoped<CreateCategoryHandler>();
builder.Services.AddScoped<GetCategoriesHandler>();
builder.Services.AddScoped<RenameCategoryHandler>();
builder.Services.AddScoped<DeactivateCategoryHandler>();
builder.Services.AddScoped<RestoreCategoryHandler>();
builder.Services.AddScoped<CreateTransactionHandler>();
builder.Services.AddScoped<GetTransactionsHandler>();
builder.Services.AddScoped<UpdateTransactionHandler>();
builder.Services.AddScoped<DeleteTransactionHandler>();
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("postgresql");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                if (!Guid.TryParse(context.Principal?.FindFirst("sub")?.Value, out var userId)) { context.Fail("Invalid subject."); return; }
                var store = context.HttpContext.RequestServices.GetRequiredService<IUserProfileStore>();
                var user = await store.FindByIdAsync(userId, context.HttpContext.RequestAborted);
                var telegramVersion = context.Principal?.FindFirst("telegram_link")?.Value;
                if (user is null || (user.EmailConfirmedAtUtc is null && (user.Email is not null || telegramVersion is null)) || context.Principal?.FindFirst("security_stamp")?.Value != user.SecurityStamp.ToString())
                { context.Fail("Session is no longer valid."); return; }
                if (telegramVersion is not null)
                {
                    var links = context.HttpContext.RequestServices.GetRequiredService<ITelegramAuthStore>();
                    var link = await links.FindLinkAsync(userId, context.HttpContext.RequestAborted);
                    if (!Guid.TryParse(telegramVersion, out var version) || link?.Version != version)
                        context.Fail("Telegram was disconnected.");
                }
            }
        };
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = jwtSigningKey,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            NameClaimType = JwtRegisteredClaimNames.Sub
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<Finance.Api.Errors.ApiExceptionHandler>();

var app = builder.Build();
var migrateOnly = builder.Configuration.GetValue<bool>("Database:MigrateOnly");
if (migrateOnly || builder.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<FinanceDbContext>().Database.MigrateAsync();
    if (migrateOnly) return;
}

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
        context.Response.OnStarting(() => { context.Response.Headers.CacheControl = "no-store"; return Task.CompletedTask; });
    var started = System.Diagnostics.Stopwatch.GetTimestamp();
    try { await next(context); }
    finally
    {
        app.Logger.LogInformation("HTTP {Method} {Path} returned {StatusCode} in {ElapsedMs} ms. Trace {TraceId}",
            context.Request.Method, context.Request.Path, context.Response.StatusCode,
            System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds, context.TraceIdentifier);
    }
});
app.UseExceptionHandler();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

if (builder.Configuration.GetValue("Http:RedirectToHttps", true)) app.UseHttpsRedirection();
app.UseHostedFrontend();

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<FinancialPrivacyMiddleware>();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

public partial class Program;
