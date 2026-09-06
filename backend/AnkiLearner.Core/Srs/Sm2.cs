using AnkiLearner.Core.Entities;

namespace AnkiLearner.Core.Srs;

/// <summary>User confidence buttons, AnkiDroid-style.</summary>
public enum ReviewGrade
{
    Again = 0,
    Hard = 1,
    Good = 2,
    Easy = 3,
}

/// <summary>
/// SM-2 spaced-repetition algorithm (spec §5, ported from the DanishLearner POC).
/// Buttons map to SM-2 quality: Again/Hard/Good/Easy → 2/3/4/5.
///
/// Textbook SM-2 derives the next interval from the ease factor alone, so Hard/Good/Easy
/// all produce the *same* interval and only diverge one review later, via the ease change.
/// We keep the SM-2 ladder as the "Good" baseline but add Anki's per-grade modifiers
/// (<see cref="HardFactor"/> / <see cref="EasyBonus"/>) so the three buttons differ at the
/// moment they are pressed, which is what the button hints promise.
/// </summary>
public static class Sm2
{
    /// <summary>"Again" reschedules within the session without touching the day interval.</summary>
    public const int AgainMinutes = 10;

    /// <summary>Hard grows the *current* interval by this instead of by ease (Anki's hard factor).</summary>
    public const double HardFactor = 1.2;

    /// <summary>Easy multiplies the Good interval by this (Anki's easy bonus).</summary>
    public const double EasyBonus = 1.3;

    /// <summary>Easy on a brand-new card jumps straight here (Anki's "easy interval").</summary>
    public const int EasyFirstIntervalDays = 4;

    /// <summary>Interval ceiling when the user has not chosen one (Anki's default, ~100 years).</summary>
    public const int DefaultMaxIntervalDays = 36500;

    private static int Quality(ReviewGrade grade) => grade switch
    {
        ReviewGrade.Again => 2,
        ReviewGrade.Hard => 3,
        ReviewGrade.Good => 4,
        ReviewGrade.Easy => 5,
        _ => 4,
    };

    /// <summary>Applies a grade to the state (mutates <paramref name="s"/>).</summary>
    public static void Apply(
        SrsState s, ReviewGrade grade, DateTime now, int maxIntervalDays = DefaultMaxIntervalDays)
    {
        var q = Quality(grade);

        if (q < 3) // forgot
        {
            s.Repetitions = 0;
            s.IntervalDays = 0;
            s.Lapses += 1;
        }
        else
        {
            var previous = s.IntervalDays;
            s.Repetitions += 1;

            // Baseline for a "Good" answer: the SM-2 ladder — 1 d, 6 d, then × ease.
            var goodDays = s.Repetitions switch
            {
                1 => 1.0,
                2 => 6.0,
                _ => previous * s.EaseFactor,
            };

            var days = grade switch
            {
                // Hard advances slowly off the current interval, but never past Good.
                ReviewGrade.Hard => Math.Min(goodDays, Math.Max(1.0, previous * HardFactor)),
                // Easy graduates a new card straight to 4 days; afterwards it adds the bonus.
                ReviewGrade.Easy => s.Repetitions == 1 ? EasyFirstIntervalDays : goodDays * EasyBonus,
                _ => goodDays,
            };

            s.IntervalDays = Math.Clamp((int)Math.Round(days), 1, Math.Max(1, maxIntervalDays));
        }

        s.EaseFactor += 0.1 - (5 - q) * (0.08 + (5 - q) * 0.02);
        if (s.EaseFactor < 1.3) s.EaseFactor = 1.3;

        s.Due = q < 3 ? now.AddMinutes(AgainMinutes) : now.AddDays(s.IntervalDays);
        s.LastReviewed = now;
    }

    /// <summary>Interval (days) this grade would produce — for button hints. Does not mutate.</summary>
    public static int PreviewDays(
        SrsState s, ReviewGrade grade, int maxIntervalDays = DefaultMaxIntervalDays)
    {
        var copy = new SrsState
        {
            IntervalDays = s.IntervalDays,
            EaseFactor = s.EaseFactor,
            Repetitions = s.Repetitions,
            Lapses = s.Lapses,
        };
        Apply(copy, grade, DateTime.UtcNow, maxIntervalDays);
        return copy.IntervalDays;
    }
}
