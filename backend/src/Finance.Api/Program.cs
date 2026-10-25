using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Text.Json.Serialization;
using Finance.Api.HealthChecks;
using Finance.Api.Security;
using Finance.Application.Abstractions;
using Finance.Application.Auth.Login;
using Finance.Application.Auth.Register;
using Finance.Application.Auth.Refresh;
using Finance.Application.Auth.Logout;
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

builder.Services.AddDbContext<FinanceDbContext>(options =>
    options.UseNpgsql(databaseConnectionString));
builder.Services.AddScoped<IUserRegistrationStore, UserRegistrationStore>();
builder.Services.AddScoped<IUserLoginStore, UserLoginStore>();
builder.Services.AddScoped<IUserProfileStore, UserProfileStore>();
builder.Services.AddScoped<ICategoryStore, CategoryStore>();
builder.Services.AddScoped<ITransactionStore, TransactionStore>();
builder.Services.AddScoped<Finance.Application.Dashboard.IDashboardStore, DashboardStore>();
builder.Services.AddScoped<Finance.Application.Dashboard.DashboardHandler>();
builder.Services.AddScoped<IRefreshSessionStore, RefreshSessionStore>();
builder.Services.AddSingleton<IPasswordHasher, AspNetCorePasswordHasher>();
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddSingleton(jwtOptions);
builder.Services.AddSingleton<IAccessTokenProvider, JwtAccessTokenProvider>();
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
app.UseExceptionHandler();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

public partial class Program;
