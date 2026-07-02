namespace AnkiLearner.Core.Abstractions;

/// <summary>
/// AI lookup result for a target-language term (spec §3.4). Dictionary keys are the
/// known-language BCP-47 codes the lookup was requested for.
/// </summary>
public record WordLookupResult(
    string Term,
    string Transcription,
    string PartOfSpeech,
    string Gender,
    Dictionary<string, List<string>> Meanings,
    string Example,
    Dictionary<string, string> ExampleTranslations);

/// <summary>
/// Looks up a word/phrase in the target language and returns structured dictionary
/// data. Implementations degrade gracefully: when not configured, <see cref="IsAvailable"/>
/// is false and callers must not invoke <see cref="LookupAsync"/> (spec FR-P3).
/// </summary>
public interface IWordLookupProvider
{
    string Name { get; }
    bool IsAvailable { get; }

    Task<WordLookupResult> LookupAsync(
        string term,
        string targetLanguage,
        IReadOnlyList<string> knownLanguages,
        CancellationToken ct);
}
