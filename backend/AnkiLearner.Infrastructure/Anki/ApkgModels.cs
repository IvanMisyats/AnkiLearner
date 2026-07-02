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
public record ApkgCard(int Ord, int Type, int Queue, long Due, int IntervalDays, int EaseFactor, int Reps, int Lapses);

public record ApkgNote(string Front, string Back, List<string> Tags, string DeckName, List<ApkgCard> Cards);

public record ApkgParseResult(
    List<ApkgNote> Notes,
    List<string> Skipped,
    DateTime CollectionCreatedUtc);
