using AnkiLearner.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AnkiLearner.Api.Services;

/// <summary>
/// After the API has started, sets up FSRS memory for every state not yet studied under FSRS
/// (spec §5): the real work happens once after the FSRS deploy; later runs only redo what an
/// earlier trigger failed to finish (unchanged states are skipped, so they cost a read). Runs in
/// the background, one user at a time, and only logs failures: it must never stop the API from
/// serving. Study works meanwhile — a card without memory is set up on the spot when graded.
/// </summary>
public class FsrsSetupOnStartup(IServiceScopeFactory scopes, ILogger<FsrsSetupOnStartup> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        List<Guid> userIds;
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            userIds = await db.SrsStates
                .Join(db.Words, s => s.WordId, w => w.Id, (s, w) => w.UserId)
                .Distinct()
                .ToListAsync(stoppingToken);
        }
        catch (Exception e) when (!stoppingToken.IsCancellationRequested)
        {
            log.LogError(e, "FSRS setup: could not list users");
            return;
        }

        foreach (var userId in userIds)
        {
            try
            {
                using var scope = scopes.CreateScope(); // a fresh DbContext per user
                var initializer = scope.ServiceProvider.GetRequiredService<FsrsInitializer>();
                var count = await initializer.SetUpAsync(userId, onlyUnset: false, stoppingToken);
                if (count > 0) log.LogInformation("FSRS setup: {Count} states for user {UserId}", count, userId);
            }
            catch (Exception e) when (!stoppingToken.IsCancellationRequested)
            {
                log.LogError(e, "FSRS setup failed for user {UserId}", userId);
            }
        }
    }
}
