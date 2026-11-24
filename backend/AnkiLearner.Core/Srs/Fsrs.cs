using AnkiLearner.Core.Entities;

namespace AnkiLearner.Core.Srs;

/// <summary>A card's FSRS memory. <see cref="Stability"/>: days until recall drops to 90%.
/// <see cref="Difficulty"/>: 1 (easy) – 10 (hard).</summary>
public readonly record struct MemoryState(double Stability, double Difficulty);

/// <summary>A card that has been reviewed: its memory and when that memory was last updated.</summary>
public readonly record struct FsrsCard(MemoryState Memory, DateTime LastReviewed);

/// <summary>The outcome of one review.</summary>
/// <param name="Retrievability">Predicted recall at review time; null for a card's first review.</param>
public sealed record FsrsReview(MemoryState Memory, int IntervalDays, DateTime Due, double? Retrievability);

/// <summary>One user's scheduler settings.</summary>
public sealed record FsrsParameters(IReadOnlyList<double> Weights, double DesiredRetention, int MaxIntervalDays)
{
    public static FsrsParameters Default { get; } =
        new(Fsrs.DefaultWeights, Fsrs.DefaultDesiredRetention, Fsrs.DefaultMaxIntervalDays);
}

/// <summary>
/// FSRS-6 spaced-repetition model (spec §5), ported from py-fsrs 6.3.3 (<c>fsrs/scheduler.py</c>).
/// Formulas, default weights, bounds and fuzz ranges are py-fsrs's. Deliberate differences:
/// <list type="bullet">
/// <item>Elapsed time is counted in <b>calendar days</b>, not 24-hour periods — Anki's optimizer
/// fits weights that way. Days start at UTC midnight (the app's day everywhere); Anki starts them at
/// 04:00 local time. Where the two boundaries don't split a user's reviews differently (verified on
/// a European collection) this reproduces Anki's memory states exactly.</item>
/// <item>Passing day intervals are kept in order, as Anki does: Good ≥ Hard + 1 day, Easy ≥ Good + 1.</item>
/// <item>One learning step, as Anki's default steps give: a new card's first Hard or Good brings it
/// back in 10 minutes, its first Easy schedules days. Again always means "again in 10 minutes";
/// any later passing grade schedules a (fuzzed) day interval. So a new word is seen at least
/// twice in its first session.</item>
/// </list>
/// </summary>
public static class Fsrs
{
    /// <summary>"Again" shows the card again within the session after this many minutes.</summary>
    public const int AgainMinutes = 10;

    /// <summary>A new card's first Hard or Good shows it again after this many minutes (one
    /// learning step) instead of scheduling days straight away.</summary>
    public const int LearningStepMinutes = 10;

    /// <summary>Interval ceiling when the user has not chosen one (Anki's default, ~100 years).</summary>
    public const int DefaultMaxIntervalDays = 36500;

    /// <summary>Target probability of recalling a card when it comes due.</summary>
    public const double DefaultDesiredRetention = 0.9;

    public const double MinDesiredRetention = 0.70;
    public const double MaxDesiredRetention = 0.97;

    public const int WeightCount = 21;

    private const double MinStability = 0.001;
    private const double MinDifficulty = 1.0;
    private const double MaxDifficulty = 10.0;

    /// <summary>py-fsrs 6.3.3 <c>DEFAULT_PARAMETERS</c>.</summary>
    public static readonly IReadOnlyList<double> DefaultWeights =
    [
        0.212, 1.2931, 2.3065, 8.2956, 6.4133, 0.8334, 3.0194, 0.001, 1.8722, 0.1666, 0.796,
        1.4835, 0.0614, 0.2629, 1.6483, 0.6014, 1.8729, 0.5425, 0.0912, 0.0658, 0.1542,
    ];

    // py-fsrs LOWER_BOUNDS_PARAMETERS / UPPER_BOUNDS_PARAMETERS.
    private static readonly double[] LowerBounds =
    [
        0.001, 0.001, 0.001, 0.001, 1.0, 0.001, 0.001, 0.001, 0.0, 0.0, 0.001,
        0.001, 0.001, 0.001, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.1,
    ];

    private static readonly double[] UpperBounds =
    [
        100.0, 100.0, 100.0, 100.0, 10.0, 4.0, 4.0, 0.75, 4.5, 0.8, 3.5,
        5.0, 0.25, 0.9, 4.0, 1.0, 6.0, 2.0, 2.0, 0.8, 0.8,
    ];

    // py-fsrs FUZZ_RANGES: (start, end, factor).
    private static readonly (double Start, double End, double Factor)[] FuzzRanges =
    [
        (2.5, 7.0, 0.15),
        (7.0, 20.0, 0.1),
        (20.0, double.PositiveInfinity, 0.05),
    ];

    /// <summary>Checks a weight set; returns an error message, or null when it is usable.</summary>
    public static string? Validate(IReadOnlyList<double> weights)
    {
        if (weights.Count != WeightCount)
            return $"Expected {WeightCount} FSRS weights, got {weights.Count}.";
        for (var i = 0; i < WeightCount; i++)
        {
            var w = weights[i];
            if (double.IsNaN(w) || w < LowerBounds[i] || w > UpperBounds[i])
                return FormattableString.Invariant(
                    $"FSRS weight {i} = {w} is outside {LowerBounds[i]}–{UpperBounds[i]}.");
        }
        return null;
    }

    /// <summary>Calendar days between two UTC times (Anki's counting): 23:50 → 00:10 is 1 day.</summary>
    public static int ElapsedDays(DateTime lastReviewed, DateTime now) =>
        Math.Max(0, (Utc(now).Date - Utc(lastReviewed).Date).Days);

    /// <summary>Predicted probability of recalling the card after <paramref name="elapsedDays"/>.</summary>
    public static double Retrievability(MemoryState memory, int elapsedDays, IReadOnlyList<double> w)
    {
        var (decay, factor) = Curve(w);
        return Math.Pow(1 + factor * elapsedDays / memory.Stability, decay);
    }

    /// <summary>Memory after a card's very first review.</summary>
    public static MemoryState Initial(ReviewGrade grade, IReadOnlyList<double> w)
    {
        var rating = Rating(grade);
        return new(Math.Max(w[rating - 1], MinStability), ClampDifficulty(InitialDifficulty(rating, w)));
    }

    /// <summary>Memory after a review <paramref name="elapsedDays"/> after the previous one.</summary>
    public static MemoryState Next(MemoryState memory, int elapsedDays, ReviewGrade grade, IReadOnlyList<double> w)
    {
        var rating = Rating(grade);
        return new(NextStability(memory, elapsedDays, rating, w), NextDifficulty(memory.Difficulty, rating, w));
    }

    /// <summary>Stability as if "Again" were pressed, with Difficulty kept — "Study again" (FR-D8).</summary>
    public static MemoryState Forget(MemoryState memory, int elapsedDays, IReadOnlyList<double> w) =>
        memory with { Stability = NextStability(memory, elapsedDays, rating: 1, w) };

    /// <summary>Days until recall is predicted to fall to the desired retention (≥ 1, ≤ the cap).</summary>
    public static int NextInterval(double stability, FsrsParameters p)
    {
        var (decay, factor) = Curve(p.Weights);
        var days = stability / factor * (Math.Pow(p.DesiredRetention, 1 / decay) - 1);
        if (double.IsNaN(days)) return 1; // only reachable with unvalidated parameters
        // Math.Round rounds half to even, like Python's round(). Clamp before the cast.
        return (int)Math.Clamp(Math.Round(days), 1, Math.Max(1, p.MaxIntervalDays));
    }

    /// <summary>
    /// Spreads an interval by a few percent (py-fsrs fuzz) so cards reviewed in one sitting don't
    /// come due on one day. <paramref name="unit"/> ∈ [0, 1) picks the day; intervals under 2.5 d
    /// are not fuzzed.
    /// </summary>
    public static int Fuzz(int intervalDays, double unit, int maxIntervalDays)
    {
        if (intervalDays < 2.5) return intervalDays;

        var delta = 1.0;
        foreach (var (start, end, factor) in FuzzRanges)
            delta += factor * Math.Max(Math.Min(intervalDays, end) - start, 0.0);

        var min = Math.Max(2, (int)Math.Round(intervalDays - delta));
        var max = Math.Min((int)Math.Round(intervalDays + delta), maxIntervalDays);
        min = Math.Min(min, max);
        return Math.Clamp((int)Math.Floor(unit * (max - min + 1)) + min, min, max);
    }

    /// <summary>
    /// Applies one grade. <paramref name="card"/> is null for a card never reviewed. "Again" brings
    /// the card back in <see cref="AgainMinutes"/>, and so does a new card's first Hard or Good
    /// (<see cref="LearningStepMinutes"/>); other grades schedule the fuzzed interval, kept in order
    /// (Hard &lt; Good &lt; Easy) like Anki so the day intervals on the buttons never read alike.
    /// </summary>
    public static FsrsReview Review(
        FsrsCard? card, ReviewGrade grade, DateTime now, FsrsParameters p, double fuzzUnit)
    {
        now = Utc(now);
        var elapsed = card is { } c0 ? ElapsedDays(c0.LastReviewed, now) : 0;
        double? retrievability = card is { } c1 ? Retrievability(c1.Memory, elapsed, p.Weights) : null;
        MemoryState MemoryAfter(ReviewGrade g) =>
            card is { } c ? Next(c.Memory, elapsed, g, p.Weights) : Initial(g, p.Weights);

        var memory = MemoryAfter(grade);
        if (grade == ReviewGrade.Again)
            return new FsrsReview(memory, 0, now.AddMinutes(AgainMinutes), retrievability);
        // One learning step: a word seen for the first time comes back within the session; the
        // same-day review after it schedules days (short-term stability formula).
        if (card is null && grade is ReviewGrade.Hard or ReviewGrade.Good)
            return new FsrsReview(memory, 0, now.AddMinutes(LearningStepMinutes), retrievability);

        int Plain(ReviewGrade g) => Fuzz(NextInterval(MemoryAfter(g).Stability, p), fuzzUnit, p.MaxIntervalDays);
        var max = Math.Max(1, p.MaxIntervalDays);
        var hard = Plain(ReviewGrade.Hard);
        var good = Math.Min(Math.Max(Plain(ReviewGrade.Good), hard + 1), max);
        var interval = grade switch
        {
            ReviewGrade.Hard => hard,
            ReviewGrade.Good => good,
            _ => Math.Min(Math.Max(Plain(ReviewGrade.Easy), good + 1), max),
        };
        return new FsrsReview(memory, interval, now.AddDays(interval), retrievability);
    }

    /// <summary>Replays a card's whole history from scratch (sorted by time, like py-fsrs's
    /// <c>reschedule_card</c>); null for an empty history.</summary>
    public static FsrsCard? Replay(IEnumerable<(DateTime At, ReviewGrade Grade)> reviews, IReadOnlyList<double> w)
    {
        FsrsCard? card = null;
        foreach (var (at, grade) in reviews.OrderBy(r => r.At))
        {
            var memory = card is { } c
                ? Next(c.Memory, ElapsedDays(c.LastReviewed, at), grade, w)
                : Initial(grade, w);
            card = new FsrsCard(memory, Utc(at));
        }
        return card;
    }

    /// <summary>
    /// A stable number in [0, 1) for fuzzing one review of one card. The same inputs always give
    /// the same number, so the interval on a button is the one the grade schedules — unless UTC
    /// midnight passes between showing the card and grading it. The time is cut to milliseconds
    /// because Postgres keeps microseconds, not .NET's 100 ns ticks. (<c>HashCode.Combine</c> is
    /// seeded per process, so it can't be used here.)
    /// </summary>
    public static double FuzzUnit(Guid wordId, ExerciseType exercise, DateTime? lastReviewed)
    {
        Span<byte> bytes = stackalloc byte[16];
        wordId.TryWriteBytes(bytes);
        var a = BitConverter.ToUInt64(bytes[..8]);
        var b = BitConverter.ToUInt64(bytes[8..]);
        var ticks = lastReviewed is { } t ? Utc(t).Ticks : 0;
        var c = (ulong)(ticks - ticks % TimeSpan.TicksPerMillisecond);
        var h = Mix(a ^ Mix(b ^ Mix((ulong)exercise ^ Mix(c))));
        return (h >> 11) * (1.0 / (1UL << 53));
    }

    // --- model internals (py-fsrs names in comments) ---

    // Again..Easy → 1..4. JSON binding accepts any integer for an enum, so check.
    private static int Rating(ReviewGrade grade) => Enum.IsDefined(grade)
        ? (int)grade + 1
        : throw new ArgumentOutOfRangeException(nameof(grade), grade, "Unknown grade.");

    // Database times come back as UTC; a Local time would split days by the server's calendar.
    private static DateTime Utc(DateTime t) => t.Kind == DateTimeKind.Local ? t.ToUniversalTime() : t;

    private static (double Decay, double Factor) Curve(IReadOnlyList<double> w)
    {
        var decay = -w[20];
        return (decay, Math.Pow(0.9, 1 / decay) - 1);
    }

    private static double ClampDifficulty(double d) => Math.Clamp(d, MinDifficulty, MaxDifficulty);

    // _initial_difficulty (unclamped)
    private static double InitialDifficulty(int rating, IReadOnlyList<double> w) =>
        w[4] - Math.Exp(w[5] * (rating - 1)) + 1;

    // _next_difficulty: linear damping, then mean reversion toward the unclamped D0(Easy).
    private static double NextDifficulty(double d, int rating, IReadOnlyList<double> w)
    {
        var delta = -(w[6] * (rating - 3));
        var damped = d + (10.0 - d) * delta / 9.0;
        var reverted = w[7] * InitialDifficulty(4, w) + (1 - w[7]) * damped;
        return ClampDifficulty(reverted);
    }

    private static double NextStability(MemoryState m, int elapsedDays, int rating, IReadOnlyList<double> w)
    {
        if (elapsedDays < 1) return ShortTermStability(m.Stability, rating, w);

        var r = Retrievability(m, elapsedDays, w);
        var s = rating == 1
            ? ForgetStability(m.Difficulty, m.Stability, r, w)
            : RecallStability(m.Difficulty, m.Stability, r, rating, w);
        return Math.Max(s, MinStability);
    }

    // _short_term_stability (same-day review)
    private static double ShortTermStability(double s, int rating, IReadOnlyList<double> w)
    {
        var increase = Math.Exp(w[17] * (rating - 3 + w[18])) * Math.Pow(s, -w[19]);
        if (rating >= 2) increase = Math.Max(increase, 1.0); // Hard/Good/Easy never lower S
        return Math.Max(s * increase, MinStability);
    }

    // _next_recall_stability
    private static double RecallStability(double d, double s, double r, int rating, IReadOnlyList<double> w)
    {
        var hardPenalty = rating == 2 ? w[15] : 1;
        var easyBonus = rating == 4 ? w[16] : 1;
        return s * (1 + Math.Exp(w[8]) * (11 - d) * Math.Pow(s, -w[9])
            * (Math.Exp((1 - r) * w[10]) - 1) * hardPenalty * easyBonus);
    }

    // _next_forget_stability: long-term formula, capped by the short-term one.
    private static double ForgetStability(double d, double s, double r, IReadOnlyList<double> w)
    {
        var longTerm = w[11] * Math.Pow(d, -w[12]) * (Math.Pow(s + 1, w[13]) - 1) * Math.Exp((1 - r) * w[14]);
        var shortTerm = s / Math.Exp(w[17] * w[18]);
        return Math.Min(longTerm, shortTerm);
    }

    // SplitMix64 finalizer.
    private static ulong Mix(ulong z)
    {
        unchecked
        {
            z += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
}
