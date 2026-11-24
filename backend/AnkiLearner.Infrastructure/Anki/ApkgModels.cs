namespace AnkiLearner.Infrastructure.Anki;

/// <summary>Thrown when the uploaded file is not a modern (v3) Anki package (spec FR-I8).</summary>
public class ApkgFormatException(string message) : Exception(message);

/// <summary>Per-card scheduling data from Anki's `cards` table (spec FR-I7).</summary>
/// <param name="Ord">0 = front→back, 1 = back→front (reversed notetypes).</param>
/// <param name="Type">0 new, 1 learning, 2 review, 3 relearning.</param>
/// <param name="Queue">-1 suspended; other values mirror Type.</param>
/// <param name="Due">For review cards: days since collection creation.</param>
/// <param name="IntervalDays">`ivl` — positive = days, negative = seconds (learning).</param>
/// <param name="EaseFactor">`factor` in permille, e.g. 2500 = 2.5.</param>
public record ApkgCard(int Ord, int Type, int Queue, long Due, int IntervalDays, int EaseFactor, int Reps, int Lapses)
{
    /// <summary>The card's review history from `revlog`, oldest first (spec FR-I9).</summary>
    public List<ApkgReview> Reviews { get; init; } = [];
}

/// <summary>One graded review from Anki's `revlog` table (spec FR-I9).</summary>
/// <param name="Id">The review time in epoch milliseconds (Anki uses it as the row id).</param>
/// <param name="Ease">The button: 1 Again, 2 Hard, 3 Good, 4 Easy.</param>
/// <param name="IntervalDays">`ivl` after the review: positive = days, negative = seconds (learning).</param>
/// <param name="LastIntervalDays">`lastIvl` before the review, same units.</param>
/// <param name="Factor">`factor` after the review, permille (0 while learning).</param>
/// <param name="Type">0 learning, 1 review, 2 relearning, 3 filtered deck (rescheduling).</param>
public record ApkgReview(long Id, int Ease, int IntervalDays, int LastIntervalDays, int Factor, int Type);

public record ApkgNote(string Front, string Back, List<string> Tags, string DeckName, List<ApkgCard> Cards);

public record ApkgParseResult(
    List<ApkgNote> Notes,
    List<string> Skipped,
    DateTime CollectionCreatedUtc);
