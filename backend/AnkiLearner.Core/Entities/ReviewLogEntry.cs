using AnkiLearner.Core.Srs;

namespace AnkiLearner.Core.Entities;

/// <summary>What happened to a card (spec FR-R11).</summary>
public enum ReviewKind
{
    /// <summary>A grade button press.</summary>
    Review,

    /// <summary>"Study again": the user forgot the word and made it due now (FR-D8).</summary>
    Relearn,

    /// <summary>Reset progress: the card's state was deleted and it is new again (FR-D8).</summary>
    Reset,

    /// <summary>The card's first FSRS memory was set up from its SM-2 state and Anki history, and
    /// it was rescheduled (spec §5). The first such row keeps the SM-2 values in its "before"
    /// columns, so the setup can be redone later from the same starting point.</summary>
    Setup,
}

/// <summary>Where a log row comes from.</summary>
public enum ReviewSource
{
    /// <summary>Recorded by this app.</summary>
    Site,

    /// <summary>Imported from an Anki review history (revlog).</summary>
    Anki,
}

/// <summary>
/// One event in a card's history: a review, a relearn or a reset (spec FR-R11). Append-only.
/// The SRS state keeps only the latest snapshot; this log keeps every step, so a scheduler can be
/// fitted to the user's history or replayed over it. State columns are null where no state
/// existed (a new card's first grade) or remains (after a reset).
/// </summary>
public class ReviewLogEntry
{
    public long Id { get; set; }

    public Guid WordId { get; set; }
    public Word Word { get; set; } = null!;

    public ExerciseType Exercise { get; set; }
    public ReviewKind Kind { get; set; }
    public ReviewSource Source { get; set; }

    /// <summary>The button pressed; null for relearn, reset and setup.</summary>
    public ReviewGrade? Grade { get; set; }

    /// <summary>When the event happened (UTC).</summary>
    public DateTime ReviewedAt { get; set; }

    /// <summary>The card's last review before this event; null if it had none.
    /// <c>ReviewedAt − PreviousReviewAt</c> is the real elapsed time, overdue days included.</summary>
    public DateTime? PreviousReviewAt { get; set; }

    public int? IntervalBefore { get; set; }
    public double? EaseBefore { get; set; }
    public DateTime? DueBefore { get; set; }

    public int? IntervalAfter { get; set; }
    public double? EaseAfter { get; set; }
    public DateTime? DueAfter { get; set; }

    // FSRS memory around the event; null in SM-2-era rows and where no memory existed.
    public double? StabilityBefore { get; set; }
    public double? DifficultyBefore { get; set; }
    public double? StabilityAfter { get; set; }
    public double? DifficultyAfter { get; set; }

    /// <summary>Predicted recall at the event (FSRS); null for a first review.</summary>
    public double? Retrievability { get; set; }

    /// <summary>Anki's revlog id (epoch ms) for rows imported from Anki; null for site rows.</summary>
    public long? AnkiRevlogId { get; set; }

    /// <summary>Builds a site row. <paramref name="before"/> is null when the card had no state;
    /// <paramref name="after"/> is null when it has none left (reset).</summary>
    public static ReviewLogEntry Create(
        Guid wordId, ExerciseType exercise, ReviewKind kind, ReviewGrade? grade, DateTime now,
        SrsSnapshot? before, SrsState? after, double? retrievability = null) => new()
    {
        WordId = wordId,
        Exercise = exercise,
        Kind = kind,
        Source = ReviewSource.Site,
        Grade = grade,
        ReviewedAt = now,
        PreviousReviewAt = before?.LastReviewed,
        IntervalBefore = before?.IntervalDays,
        EaseBefore = before?.EaseFactor,
        DueBefore = before?.Due,
        IntervalAfter = after?.IntervalDays,
        EaseAfter = after?.EaseFactor,
        DueAfter = after?.Due,
        StabilityBefore = before?.Stability,
        DifficultyBefore = before?.Difficulty,
        StabilityAfter = after?.Stability,
        DifficultyAfter = after?.Difficulty,
        Retrievability = retrievability,
    };
}

/// <summary>A copy of the scheduling fields of an <see cref="SrsState"/>, taken before it changes.</summary>
public sealed record SrsSnapshot(
    int IntervalDays, double EaseFactor, DateTime Due, DateTime? LastReviewed,
    double? Stability, double? Difficulty)
{
    public static SrsSnapshot Of(SrsState s) =>
        new(s.IntervalDays, s.EaseFactor, s.Due, s.LastReviewed, s.Stability, s.Difficulty);

    /// <summary>For a state whose memory was worked out on the spot (not set up yet): log the
    /// memory that was actually used, not the empty columns.</summary>
    public SrsSnapshot WithMemory(FsrsCard? card) =>
        Stability is null && card is { } c
            ? this with { Stability = c.Memory.Stability, Difficulty = c.Memory.Difficulty, LastReviewed = c.LastReviewed }
            : this;
}
