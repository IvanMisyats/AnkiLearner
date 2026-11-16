using System.Text.RegularExpressions;
using AnkiLearner.Core.Abstractions;
using AnkiLearner.Core.Entities;

namespace AnkiLearner.Core;

/// <summary>
/// The deterministic rules applied to a provider's raw Translate result (spec §3.8). Pure: the
/// AI output is untrusted, so everything the response relies on is fixed up here.
/// </summary>
public static partial class TranslateShaper
{
    /// <summary>Source "language" for input in none of the user's languages.</summary>
    public const string OtherLanguage = "other";

    /// <summary>Longest input after <see cref="CleanText"/>: one sentence.</summary>
    public const int MaxTextLength = 300;

    /// <summary>The schema cannot cap array sizes, so the caps live here.</summary>
    public const int MaxCards = 5;
    public const int MaxMeaningsPerLanguage = 4;

    /// <summary>The input as the provider sees it: invisible format characters removed (soft
    /// hyphen, direction marks, zero-width characters, BOM), control characters turned into
    /// spaces, every whitespace run collapsed to one space, trimmed.</summary>
    public static string CleanText(string text)
    {
        var visible = FormatCharactersRegex().Replace(text, string.Empty);
        visible = ControlCharactersRegex().Replace(visible, " ");
        return WhitespaceRunRegex().Replace(visible, " ").Trim();
    }

    /// <summary>Closing quotes/brackets and sentence punctuation at the end ("Tak for mad!" →
    /// "Tak for mad"): they do not make a different dictionary entry.</summary>
    public static string TrimTrailingPunctuation(string text) => text.TrimEnd(TrailingPunctuation);

    private static readonly char[] TrailingPunctuation =
        [' ', '.', '!', '?', '…', '"', '”', '»', '\'', '’', ')', ']'];

    /// <param name="raw">The provider's result.</param>
    /// <param name="text">The input as sent to the provider (<see cref="CleanText"/>).</param>
    /// <param name="sourceOverride">The language the user picked; null/empty = auto-detected.</param>
    /// <param name="learning">The user's learning language (the cards' language).</param>
    /// <param name="known">The user's known languages.</param>
    public static (TranslateResult Result, TranslateCard? WholeInput) Shape(
        TranslateResult raw, string text, string? sourceOverride, string learning, IReadOnlyList<string> known)
    {
        // 1. The user's choice wins. Anything outside the user's languages is treated as the
        //    learning language, like the provider's parser does. The input kind is the model's:
        //    a sentence can be one token ("Tak!", or any Japanese sentence).
        var source = string.IsNullOrEmpty(sourceOverride) ? raw.SourceLanguage : sourceOverride;
        if (source != OtherLanguage && source != learning && !known.Contains(source))
            source = learning;
        var kind = raw.InputKind;

        // 2. One rendering per language; the source slot is the user's own text.
        var inputTranslations = known.Prepend(learning).Distinct().ToDictionary(
            code => code,
            code => raw.InputTranslations.GetValueOrDefault(code)?.Trim() ?? string.Empty);
        if (source != OtherLanguage)
            inputTranslations[source] = text;

        // 6. Unsupported language: nothing to learn from (the API answers 422).
        if (source == OtherLanguage)
            return (new TranslateResult(source, kind, inputTranslations, []), null);

        // 3. Clean the cards. The term becomes plain text. Dedupe on the exact normalized term,
        //    not the match key, so homographs such as "gift" (married) and "en gift" (poison) both
        //    survive. A repeated term is the same citation form: its senses join the first card's
        //    meanings, even once the card cap is reached.
        var cards = new List<TranslateCard>();
        foreach (var card in raw.Cards)
        {
            var term = TermNormalizer.PlainText(card.Term ?? string.Empty);
            if (term.Length == 0) continue; // empty, or nothing but HTML

            var normalized = term.ToLowerInvariant();
            var index = cards.FindIndex(c => c.Term.ToLowerInvariant() == normalized);
            if (index >= 0)
                cards[index] = cards[index] with
                {
                    Meanings = known.ToDictionary(
                        k => k,
                        k => CleanMeanings(cards[index].Meanings[k].Concat(card.Meanings?.GetValueOrDefault(k) ?? []))),
                };
            else if (cards.Count < MaxCards)
                cards.Add(CleanCard(card, term, known));
        }

        // 4. Sentence input: the user's own sentence is the best example (and memory hook).
        //    Input in a known language uses its learning-language translation, with the
        //    original as that language's example translation (already in inputTranslations).
        var learningRendering = inputTranslations[learning];
        if (kind == InputKind.Sentence && learningRendering.Length > 0)
        {
            cards = cards.Select(c => c with
            {
                Example = learningRendering,
                ExampleTranslations = known.ToDictionary(k => k, k => inputTranslations[k]),
            }).ToList();
        }

        // 5. Multi-word phrases and sentences can be saved whole, unless a card already is that
        //    entry. A single token never is a whole input: its card is the entry. Trailing
        //    punctuation is ignored when comparing, and a phrase is saved without it; a sentence
        //    keeps it as typed.
        TranslateCard? wholeInput = null;
        var bareRendering = TrimTrailingPunctuation(learningRendering);
        if (kind != InputKind.Word && text.Any(char.IsWhiteSpace) && bareRendering.Length > 0)
        {
            var key = TermNormalizer.MatchKey(bareRendering, learning);
            if (!cards.Any(c => TermNormalizer.MatchKey(TrimTrailingPunctuation(c.Term), learning) == key))
            {
                wholeInput = new TranslateCard(
                    kind == InputKind.Phrase ? bareRendering : learningRendering,
                    Transcription: string.Empty,
                    PartOfSpeech: string.Empty,
                    Gender: string.Empty,
                    Meanings: known.ToDictionary(
                        k => k,
                        k => inputTranslations[k].Length > 0 ? new List<string> { inputTranslations[k] } : []),
                    Example: string.Empty,
                    ExampleTranslations: known.ToDictionary(k => k, _ => string.Empty),
                    Note: string.Empty);
            }
        }

        return (new TranslateResult(source, kind, inputTranslations, cards), wholeInput);
    }

    private static TranslateCard CleanCard(TranslateCard card, string term, IReadOnlyList<string> known) =>
        new(
            term,
            Clamp(card.Transcription, Word.TranscriptionMaxLength),
            Clamp(card.PartOfSpeech, Word.PartOfSpeechMaxLength),
            Clamp(card.Gender, Word.GenderMaxLength),
            known.ToDictionary(k => k, k => CleanMeanings(card.Meanings?.GetValueOrDefault(k) ?? [])),
            card.Example?.Trim() ?? string.Empty,
            known.ToDictionary(k => k, k => card.ExampleTranslations?.GetValueOrDefault(k)?.Trim() ?? string.Empty),
            card.Note?.Trim() ?? string.Empty);

    /// <summary>Trimmed, non-empty, distinct (case-insensitive), in order, at most 4.</summary>
    private static List<string> CleanMeanings(IEnumerable<string?> meanings) =>
        meanings
            .Select(m => m?.Trim() ?? string.Empty)
            .Where(m => m.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxMeaningsPerLanguage)
            .ToList();

    private static string Clamp(string? value, int maxLength)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength].TrimEnd();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRunRegex();

    // Unicode "format" characters: invisible, and not whitespace to \s.
    [GeneratedRegex(@"\p{Cf}")]
    private static partial Regex FormatCharactersRegex();

    // Control characters (NUL, BEL, ...; tabs and line breaks too).
    [GeneratedRegex(@"\p{Cc}")]
    private static partial Regex ControlCharactersRegex();
}
