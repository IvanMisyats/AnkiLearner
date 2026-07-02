using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace AnkiLearner.Tests.Integration;

/// <summary>
/// Boots the API against a throwaway Postgres container. EF migrations run on app startup.
/// Requires Docker to be running.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:16").Build();

    public async Task InitializeAsync() => await _db.StartAsync();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", _db.GetConnectionString());
        // Tests fire many auth requests from one "IP"; don't trip the limiter.
        builder.UseSetting("RateLimiting:AuthPerMinute", "1000");
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _db.DisposeAsync();
    }
}
