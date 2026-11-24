using AnkiLearner.Core.Entities;
using AnkiLearner.Core.Srs;
using AnkiLearner.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AnkiLearner.Api.Services;

/// <summary>
/// Sets up FSRS memory for states that predate FSRS, and reschedules them (spec §5, "Setting up
/// FSRS"). A state can be set up again as long as it has no FSRS-era review, so this runs after
/// deploy, after an Anki history import and after a change of weights or retention — and never
/// touches a card the user has studied since.
/// </summary>
public class FsrsInitializer(AppDbContext db, SettingsService settingsService, ILogger<FsrsInitializer> log)
{
    /// <summary>
    /// <see cref="SetUpAsync"/> for a trigger whose own change is already saved (an import, a
    /// settings change): a failure is logged, not thrown, and a client disconnect doesn't cancel
    /// it. The after-startup run redoes whatever this didn't finish.
    /// </summary>
    public async Task TrySetUpAsync(Guid userId, bool onlyUnset)
    {
        try
        {
            await SetUpAsync(userId, onlyUnset, CancellationToken.None);
        }
        catch (Exception e)
        {
            log.LogError(e, "FSRS setup failed for user {UserId}", userId);
        }
    }

    /// <summary>The log facts setup needs about one card.</summary>
    private sealed record CardLog(
        long Id, ReviewKind Kind, ReviewSource Source, DateTime ReviewedAt, ReviewGrade? Grade,
        int? IntervalBefore, DateTime? DueBefore, DateTime? PreviousReviewAt, bool HasStability);

    /// <summary>
    /// Sets up the user's eligible states and saves them, one transaction. With
    /// <paramref name="onlyUnset"/> only states without memory are considered; otherwise also
    /// states set up before (history import, new weights, the after-startup run). A state whose
    /// result is unchanged is left alone. Returns how many states changed.
    /// </summary>
    public async Task<int> SetUpAsync(Guid userId, bool onlyUnset, CancellationToken ct)
    {
        var p = (await settingsService.GetOrCreateAsync(userId, ct)).SchedulerParameters();

        var query = db.SrsStates.Where(s => db.Words.Any(w => w.Id == s.WordId && w.UserId == userId));
        if (onlyUnset) query = query.Where(s => s.Stability == null);
        var states = await query.ToListAsync(ct);
        if (states.Count == 0) return 0;

        var wordIds = states.Select(s => s.WordId).Distinct().ToList();
        var wordCreated = await db.Words.Where(w => wordIds.Contains(w.Id))
            .ToDictionaryAsync(w => w.Id, w => w.CreatedAt, ct);
        var logs = (await db.ReviewLog.AsNoTracking()
                .Where(r => wordIds.Contains(r.WordId))
                .Select(r => new
                {
                    r.WordId, r.Exercise,
                    Log = new CardLog(r.Id, r.Kind, r.Source, r.ReviewedAt, r.Grade,
                        r.IntervalBefore, r.DueBefore, r.PreviousReviewAt, r.StabilityAfter != null),
                })
                .ToListAsync(ct))
            .ToLookup(x => (x.WordId, x.Exercise), x => x.Log);

        var now = DateTime.UtcNow;
        var rows = new Dictionary<SrsState, ReviewLogEntry>();
        foreach (var state in states)
        {
            var log = logs[(state.WordId, state.Exercise)].ToList();
            if (SetUp(state, log, wordCreated[state.WordId], p, now) is { } entry)
            {
                db.ReviewLog.Add(entry);
                rows[state] = entry;
            }
        }

        // One transaction. A state graded (or set up by another run) since it was loaded fails
        // its row-version check: drop that state and its Setup row, and save the rest.
        while (true)
        {
            try
            {
                await db.SaveChangesAsync(ct);
                return rows.Count;
            }
            catch (DbUpdateConcurrencyException e)
            {
                var conflicts = e.Entries.Select(x => x.Entity).OfType<SrsState>().ToList();
                if (conflicts.Count == 0) throw;
                foreach (var state in conflicts)
                {
                    db.Entry(state).State = EntityState.Detached;
                    if (rows.Remove(state, out var row)) db.Entry(row).State = EntityState.Detached;
                }
            }
        }
    }

    /// <summary>
    /// A state's FSRS memory: the stored one, or — for a card graded, previewed or relearned
    /// before the after-deploy run reached it — the one setup would give it now (nothing saved).
    /// Null when the card has never been reviewed.
    /// </summary>
    public async Task<FsrsCard?> CurrentMemoryAsync(SrsState state, FsrsParameters p, CancellationToken ct)
    {
        if (state is { Stability: { } s, Difficulty: { } d, LastReviewed: { } last })
            return new FsrsCard(new MemoryState(s, d), last);

        var log = await db.ReviewLog.AsNoTracking()
            .Where(r => r.WordId == state.WordId && r.Exercise == state.Exercise)
            .Select(r => new CardLog(r.Id, r.Kind, r.Source, r.ReviewedAt, r.Grade,
                r.IntervalBefore, r.DueBefore, r.PreviousReviewAt, r.StabilityAfter != null))
            .ToListAsync(ct);
        var wordCreated = await db.Words.Where(w => w.Id == state.WordId).Select(w => w.CreatedAt).FirstAsync(ct);
        return Compute(state, CurrentLife(log, state, wordCreated), p.Weights);
    }

    /// <summary>Sets up one state in place; returns the Setup log row, or null if the state is
    /// not eligible or was never reviewed.</summary>
    private static ReviewLogEntry? SetUp(
        SrsState state, List<CardLog> log, DateTime wordCreated, FsrsParameters p, DateTime now)
    {
        var life = CurrentLife(log, state, wordCreated);
        // Studied under FSRS already: its memory comes from real reviews now.
        if (life.Any(r => r.HasStability && r.Kind is ReviewKind.Review or ReviewKind.Relearn)) return null;

        if (Compute(state, life, p.Weights) is not { } card) return null;

        var before = SrsSnapshot.Of(state);
        var interval = Fsrs.Fuzz(Fsrs.NextInterval(card.Memory.Stability, p),
            Fsrs.FuzzUnit(state.WordId, state.Exercise, card.LastReviewed), p.MaxIntervalDays);
        var due = card.LastReviewed.AddDays(interval);
        // Same result as last time (a re-run, a setting that changes nothing here): no new row.
        if (state.Stability == card.Memory.Stability && state.Difficulty == card.Memory.Difficulty
            && state.LastReviewed == card.LastReviewed && state.IntervalDays == interval && state.Due == due)
            return null;

        state.Stability = card.Memory.Stability;
        state.Difficulty = card.Memory.Difficulty;
        state.LastReviewed = card.LastReviewed;
        state.IntervalDays = interval;
        state.Due = due; // in the past = due now

        return ReviewLogEntry.Create(state.WordId, state.Exercise, ReviewKind.Setup, grade: null, now, before, state);
    }

    /// <summary>
    /// The card's log since its state last started over: after the latest reset. For a state
    /// created on the site, Anki history older than the state is stale too (the word was reset or
    /// re-added before the log existed). An imported state is dated back to the Anki collection's
    /// creation, before its word — its whole Anki history belongs to it.
    /// </summary>
    private static List<CardLog> CurrentLife(List<CardLog> log, SrsState state, DateTime wordCreated)
    {
        var lastReset = log.Where(r => r.Kind == ReviewKind.Reset).Select(r => (DateTime?)r.ReviewedAt).Max();
        var historyFrom = state.CreatedAt < wordCreated ? DateTime.MinValue : state.CreatedAt;
        return log
            .Where(r => lastReset is null || r.ReviewedAt > lastReset)
            .Where(r => r.Source != ReviewSource.Anki || r.ReviewedAt >= historyFrom)
            .OrderBy(r => r.ReviewedAt).ThenBy(r => r.Id)
            .ToList();
    }

    private static FsrsCard? Compute(SrsState state, List<CardLog> life, IReadOnlyList<double> w)
    {
        // A state set up before has its SM-2 values in the first Setup row.
        var firstSetup = life.FirstOrDefault(r => r.Kind == ReviewKind.Setup);
        var legacy = firstSetup is null
            ? new LegacyCard(state.IntervalDays, state.Lapses, state.Repetitions, state.LastReviewed, state.Due)
            : new LegacyCard(firstSetup.IntervalBefore ?? 0, state.Lapses, state.Repetitions,
                firstSetup.PreviousReviewAt, firstSetup.DueBefore ?? state.Due);

        var history = life
            .Where(r => r.Source == ReviewSource.Anki && r.Kind == ReviewKind.Review && r.Grade is not null)
            .Select(r => new HistoryReview(r.ReviewedAt, r.Grade!.Value, r.IntervalBefore))
            .ToList();
        return FsrsSetup.MemoryFor(legacy, history, w);
    }
}
