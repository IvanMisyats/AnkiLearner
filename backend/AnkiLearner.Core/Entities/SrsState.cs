namespace AnkiLearner.Core.Entities;

/// <summary>Study direction/mode. Each type tracks its own SRS progress per word (spec §4.1).</summary>
public enum ExerciseType
{
    /// <summary>Prompt: target term → recall the known-language translations.</summary>
    TargetToKnown,

    /// <summary>Prompt: known-language translations → recall the target term.</summary>
    KnownToTarget,

    // Future game exercises (spec §10): Typing, Anagram, Crossword.
}

/// <summary>
/// Spaced-repetition state (FSRS memory + schedule). One row = one word in one exercise type.
/// A word with no row for an exercise is "new" for that exercise.
/// </summary>
public class SrsState
{
    public Guid Id { get; set; }
    public Guid WordId { get; set; }
    public Word Word { get; set; } = null!;

    public ExerciseType Exercise { get; set; }

    /// <summary>When the word should be shown again.</summary>
    public DateTime Due { get; set; }

    /// <summary>Current interval in days.</summary>
    public int IntervalDays { get; set; }

    /// <summary>FSRS stability: days until recall is predicted to drop to 90%. Null until the
    /// card has an FSRS memory (a new card, or an SM-2-era state not yet set up).</summary>
    public double? Stability { get; set; }

    /// <summary>FSRS difficulty, 1 (easy) – 10 (hard). Null together with <see cref="Stability"/>.</summary>
    public double? Difficulty { get; set; }

    /// <summary>Legacy SM-2 ease factor (start 2.5, floor 1.3). Kept as input for setting up FSRS;
    /// FSRS doesn't change it.</summary>
    public double EaseFactor { get; set; } = 2.5;

    /// <summary>Legacy SM-2 count of consecutive successful reviews. FSRS doesn't change it.</summary>
    public int Repetitions { get; set; }

    /// <summary>How many times the word was forgotten.</summary>
    public int Lapses { get; set; }

    public DateTime? LastReviewed { get; set; }

    /// <summary>Row version (Postgres <c>xmin</c>): a save of a state changed by someone else
    /// since it was read fails instead of overwriting it.</summary>
    public uint Version { get; set; }

    /// <summary>When this state was first created — i.e. when the word was introduced
    /// for this exercise. Used to enforce the daily new-word limit (spec FR-R7).</summary>
    public DateTime CreatedAt { get; set; }
}
