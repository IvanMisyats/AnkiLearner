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
/// SM-2 spaced-repetition state. One row = one word in one exercise type.
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

    /// <summary>Ease factor. Starts at 2.5, never below 1.3.</summary>
    public double EaseFactor { get; set; } = 2.5;

    /// <summary>Consecutive successful reviews.</summary>
    public int Repetitions { get; set; }

    /// <summary>How many times the word was forgotten.</summary>
    public int Lapses { get; set; }

    public DateTime? LastReviewed { get; set; }

    /// <summary>When this state was first created — i.e. when the word was introduced
    /// for this exercise. Used to enforce the daily new-word limit (spec FR-R7).</summary>
    public DateTime CreatedAt { get; set; }
}
