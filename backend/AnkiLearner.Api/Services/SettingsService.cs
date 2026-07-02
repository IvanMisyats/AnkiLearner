using AnkiLearner.Core.Entities;
using AnkiLearner.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AnkiLearner.Api.Services;

/// <summary>
/// Loads (or lazily creates) a user's settings row. Registration is not atomic with
/// settings creation, so callers must tolerate a missing row — this centralizes that.
/// </summary>
public class SettingsService(AppDbContext db)
{
    public async Task<UserSettings> GetOrCreateAsync(Guid userId, CancellationToken ct)
    {
        var settings = await db.UserSettings.FirstOrDefaultAsync(s => s.UserId == userId, ct);
        if (settings is null)
        {
            settings = new UserSettings
            {
                UserId = userId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };
            db.UserSettings.Add(settings);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // A parallel request created the row first — use theirs.
                db.Entry(settings).State = EntityState.Detached;
                settings = await db.UserSettings.FirstAsync(s => s.UserId == userId, ct);
            }
        }
        return settings;
    }
}
