using System.Text;
using System.Threading.RateLimiting;
using AnkiLearner.Api.Auth;
using AnkiLearner.Core.Abstractions;
using AnkiLearner.Infrastructure.Data;
using AnkiLearner.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

// --- Database ---
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is required.");
builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));

// --- Identity (user store only; auth is JWT, not cookies) ---
builder.Services.AddIdentityCore<AppUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 8;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireDigit = false;
    })
    .AddEntityFrameworkStores<AppDbContext>();

// --- JWT authentication ---
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
if (jwt.SigningKey.Length < 32)
    throw new InvalidOperationException(
        "Jwt:SigningKey must be set and at least 32 characters. Generate a random one per instance.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o => o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = jwt.Issuer,
        ValidAudience = jwt.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
        ClockSkew = TimeSpan.FromSeconds(30),
    });
builder.Services.AddAuthorization();

builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<RefreshTokenService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<AnkiLearner.Api.Services.SettingsService>();
builder.Services.AddSingleton<IContentSanitizer, AnkiLearner.Infrastructure.ContentSanitizer>();

// --- AI word lookup (server-level key; degrades gracefully without one) ---
builder.Services.Configure<AnkiLearner.Infrastructure.Lookup.AnthropicOptions>(
    builder.Configuration.GetSection("Anthropic"));
// The bare ANTHROPIC_API_KEY env var also works (documented in README);
// Anthropic:ApiKey / Anthropic__ApiKey take precedence when set.
builder.Services.PostConfigure<AnkiLearner.Infrastructure.Lookup.AnthropicOptions>(o =>
{
    if (string.IsNullOrWhiteSpace(o.ApiKey))
        o.ApiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? string.Empty;
});
builder.Services.AddSingleton<IWordLookupProvider, AnkiLearner.Infrastructure.Lookup.ClaudeLookupProvider>();

// --- Rate limiting for credential endpoints (spec §8) ---
// NOTE for the deploy phase: behind a reverse proxy, RemoteIpAddress is the proxy's
// address — configure UseForwardedHeaders there or all users share one rate bucket.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = builder.Configuration.GetValue("RateLimiting:AuthPerMinute", 20),
        }));
    // AI lookup costs money per call — limit per authenticated user.
    o.AddPolicy("lookup", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = builder.Configuration.GetValue("RateLimiting:LookupPerMinute", 20),
        }));
});

builder.Services.AddHealthChecks();

var app = builder.Build();

// Apply EF migrations on startup — single-instance deployment (spec §8).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthentication();
app.UseAuthorization();
// After authentication so per-user rate-limit partitions see the JWT identity.
app.UseRateLimiter();

app.MapControllers();
app.MapHealthChecks("/api/health");

app.Run();

// Exposes the entry point to WebApplicationFactory in integration tests.
public partial class Program;
