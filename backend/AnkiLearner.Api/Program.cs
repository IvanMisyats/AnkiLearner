using System.Text;
using System.Threading.RateLimiting;
using AnkiLearner.Api.Auth;
using AnkiLearner.Core.Abstractions;
using AnkiLearner.Infrastructure.Data;
using AnkiLearner.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(
        new System.Text.Json.Serialization.JsonStringEnumConverter()));
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

// --- Reverse proxy ---
// In production nginx terminates TLS and proxies to this container, so RemoteIpAddress would
// otherwise be the proxy's address: every user would share one rate-limit bucket, and
// Request.Scheme would be http, which breaks the Secure refresh cookie.
// KnownProxies/KnownNetworks are cleared deliberately: the container port is bound to loopback
// on the host and only nginx can reach it, and nginx appends the real client to
// X-Forwarded-For, which (with the default ForwardLimit of 1) is the entry taken here.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

// --- Rate limiting for credential endpoints (spec §8) ---
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
builder.Services.AddMemoryCache(); // staged .apkg imports between preview and commit

var app = builder.Build();

// Apply EF migrations on startup — single-instance deployment (spec §8).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

// Must run before anything that reads the client IP or the scheme.
app.UseForwardedHeaders();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// --- Angular SPA, served from wwwroot (single origin: no CORS needed) ---
// In development the SPA is served by `ng serve` and wwwroot is empty; these are then no-ops.
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        // Angular builds with outputHashing: all, so asset filenames change on every build and
        // may be cached indefinitely. index.html must NOT be, or clients pin an old bundle.
        var headers = ctx.Context.Response.Headers;
        headers.CacheControl =
            ctx.File.Name.Equals("index.html", StringComparison.OrdinalIgnoreCase)
                ? "no-cache, no-store, must-revalidate"
                : "public, max-age=31536000, immutable";
    },
});

// Explicit, and deliberately AFTER the static file middleware.
//
// WebApplication auto-inserts UseRouting at the START of the pipeline when it sees mapped
// endpoints. That would let the MapFallbackToFile endpoint below be selected before
// UseStaticFiles runs — and StaticFileMiddleware skips any request that already has an endpoint.
// The result is that every asset (main-*.js, styles-*.css, favicon) is answered with index.html
// and the SPA never boots. Calling UseRouting here suppresses the automatic one and pins the
// order. Verified by asserting Content-Type on a hashed asset; see the smoke test in
// infra/README.md.
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();
// After authentication so per-user rate-limit partitions see the JWT identity.
app.UseRateLimiter();

app.MapControllers();
app.MapHealthChecks("/api/health");

// Client-side routing: hand any non-API path to the SPA. The regex keeps unmatched /api/*
// requests returning a real 404 instead of an HTML page.
app.MapFallbackToFile("{*path:regex(^(?!api/).*$)}", "index.html");

app.Run();

// Exposes the entry point to WebApplicationFactory in integration tests.
public partial class Program;
