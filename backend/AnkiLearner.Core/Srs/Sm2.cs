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
/// </summary>
public static class Sm2
{
    /// <summary>"Again" reschedules within the session without touching the day interval.</summary>
    public const int AgainMinutes = 10;

    private static int Quality(ReviewGrade grade) => grade switch
    {
        ReviewGrade.Again => 2,
        ReviewGrade.Hard => 3,
        ReviewGrade.Good => 4,
        ReviewGrade.Easy => 5,
        _ => 4,
    };

    /// <summary>Applies a grade to the state (mutates <paramref name="s"/>).</summary>
    public static void Apply(SrsState s, ReviewGrade grade, DateTime now)
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
            s.Repetitions += 1;
            s.IntervalDays = s.Repetitions switch
            {
                1 => 1,
                2 => 6,
                _ => Math.Max(1, (int)Math.Round(s.IntervalDays * s.EaseFactor)),
            };
        }

        s.EaseFactor += 0.1 - (5 - q) * (0.08 + (5 - q) * 0.02);
        if (s.EaseFactor < 1.3) s.EaseFactor = 1.3;

        s.Due = q < 3 ? now.AddMinutes(AgainMinutes) : now.AddDays(s.IntervalDays);
        s.LastReviewed = now;
    }

    /// <summary>Interval (days) this grade would produce — for button hints. Does not mutate.</summary>
    public static int PreviewDays(SrsState s, ReviewGrade grade)
    {
        var copy = new SrsState
        {
            IntervalDays = s.IntervalDays,
            EaseFactor = s.EaseFactor,
            Repetitions = s.Repetitions,
            Lapses = s.Lapses,
        };
        Apply(copy, grade, DateTime.UtcNow);
        return copy.IntervalDays;
    }
}
