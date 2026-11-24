namespace AnkiLearner.Core.Srs;

/// <summary>The SM-2-era fields of a state that has no FSRS memory yet.</summary>
public sealed record LegacyCard(int IntervalDays, int Lapses, int Repetitions, DateTime? LastReviewed, DateTime Due);

/// <summary>One review from an imported Anki history. <paramref name="IntervalBefore"/> is the
/// interval in days before the review (null while the card was still learning).</summary>
public sealed record HistoryReview(DateTime At, ReviewGrade Grade, int? IntervalBefore);

/// <summary>
/// Gives a card that predates FSRS its first memory state (spec §5, "Setting up FSRS"). Anki
/// history is the best source where it exists; the SM-2 snapshot is newer where the card was
/// reviewed on the site after the import. Pure — the caller loads the inputs and saves the result.
/// </summary>
public static class FsrsSetup
{
    /// <summary>
    /// Memory for a legacy card, or null when it was never reviewed (FSRS then treats its first
    /// grade as a new card's). <paramref name="ankiHistory"/> must already be filtered to this
    /// card and ordered by time.
    /// </summary>
    public static FsrsCard? MemoryFor(LegacyCard card, IReadOnlyList<HistoryReview> ankiHistory, IReadOnlyList<double> w)
    {
        if (ankiHistory.Count > 0)
        {
            var replayed = Fsrs.Replay(ankiHistory.Select(h => (h.At, h.Grade)), w)!.Value;

            // Not reviewed on the site since the import, or reviewed in Anki again after that:
            // the history is the newer, fuller story.
            if (card.LastReviewed is not { } siteReviewed || replayed.LastReviewed > siteReviewed)
                return replayed;

            // Reviewed on the site: Difficulty from the history, raised by each lapse since the
            // import; Stability and date from the newer snapshot.
            var ankiLapses = ankiHistory.Count(h => h.Grade == ReviewGrade.Again && h.IntervalBefore > 0);
            var siteLapses = Math.Max(0, card.Lapses - ankiLapses);
            var difficulty = replayed.Memory.Difficulty;
            if (siteLapses > 0)
                difficulty = AfterLapses(difficulty, siteLapses, card.Repetitions, w);
            return new FsrsCard(new MemoryState(SnapshotStability(card, w), difficulty), siteReviewed);
        }

        // No history and no sign of a review: never graded here, and no interval imported from
        // Anki. (Lapses don't count: "Study again" adds one even to a never-studied direction.)
        if (card.LastReviewed is null && card.IntervalDays == 0) return null;

        var last = card.LastReviewed ?? card.Due.AddDays(-card.IntervalDays);
        return new FsrsCard(new MemoryState(SnapshotStability(card, w), HeuristicDifficulty(card.Lapses, card.Repetitions, w)), last);
    }

    /// <summary>
    /// Difficulty from counts alone: a first Good, one Again step per lapse, then up to three Good
    /// steps for the successes since. On a real 1.4k-card collection this matched the Difficulty
    /// replayed from full history (median error 0).
    /// </summary>
    public static double HeuristicDifficulty(int lapses, int repetitions, IReadOnlyList<double> w) =>
        AfterLapses(Fsrs.Initial(ReviewGrade.Good, w).Difficulty, lapses, repetitions, w);

    private static double AfterLapses(double difficulty, int lapses, int repetitions, IReadOnlyList<double> w)
    {
        for (var i = 0; i < lapses; i++) difficulty = AfterGrade(difficulty, ReviewGrade.Again, w);
        for (var i = 0; i < Math.Min(repetitions, 3); i++) difficulty = AfterGrade(difficulty, ReviewGrade.Good, w);
        return difficulty;
    }

    // The Difficulty update doesn't depend on Stability or elapsed time.
    private static double AfterGrade(double difficulty, ReviewGrade grade, IReadOnlyList<double> w) =>
        Fsrs.Next(new MemoryState(1, difficulty), 0, grade, w).Difficulty;

    /// <summary>The SM-2 interval read as Stability: right for a card SM-2 scheduled at about 90%
    /// recall, low for one whose site reviews went well. A low start costs one early review,
    /// whose success the model then rewards more.</summary>
    private static double SnapshotStability(LegacyCard card, IReadOnlyList<double> w) =>
        Math.Max(card.IntervalDays, Fsrs.Initial(ReviewGrade.Again, w).Stability);
}
