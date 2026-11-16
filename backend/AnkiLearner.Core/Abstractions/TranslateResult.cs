namespace AnkiLearner.Core.Abstractions;

/// <summary>What the user typed into Translate (spec §3.8).</summary>
public enum InputKind
{
    Word,
    Phrase,
    Sentence,
}

/// <summary>
/// One learning-language dictionary entry suggested by Translate. Dictionary keys are the
/// known-language codes; <see cref="Note"/> is a short usage hint in the first known language.
/// </summary>
public record TranslateCard(
    string Term,
    string Transcription,
    string PartOfSpeech,
    string Gender,
    Dictionary<string, List<string>> Meanings,
    string Example,
    Dictionary<string, string> ExampleTranslations,
    string Note);

/// <summary>
/// Translate result (spec §3.8). <see cref="SourceLanguage"/> is the learning language, a known
/// language or <c>"other"</c>; <see cref="InputTranslations"/> holds the whole input rendered in
/// the learning language and every known language.
/// </summary>
public record TranslateResult(
    string SourceLanguage,
    InputKind InputKind,
    Dictionary<string, string> InputTranslations,
    List<TranslateCard> Cards);
