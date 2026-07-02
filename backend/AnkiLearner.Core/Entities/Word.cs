namespace AnkiLearner.Core.Entities;

/// <summary>
/// A dictionary entry: a word or phrase in the target language plus metadata and
/// per-known-language translations (spec §4.1). All HTML fields are sanitized on save.
/// </summary>
public class Word
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    /// <summary>The word's target language (the user's learning language at creation).
    /// Switching the learning language hides words of other languages, never deletes them.</summary>
    public string LanguageCode { get; set; } = string.Empty;

    /// <summary>Target-language word/phrase. Sanitized HTML.</summary>
    public string Term { get; set; } = string.Empty;

    /// <summary>Plain-text lowercase form of <see cref="Term"/> used for duplicate detection.</summary>
    public string TermNormalized { get; set; } = string.Empty;

    /// <summary>IPA, e.g. [ˈhunˀ].</summary>
    public string? Transcription { get; set; }

    public string? PartOfSpeech { get; set; }

    /// <summary>Article/gender marker, target-language specific (e.g. "en"/"et" for Danish).</summary>
    public string? Gender { get; set; }

    /// <summary>Example sentence in the target language. Sanitized HTML.</summary>
    public string? Example { get; set; }

    /// <summary>Free-form notes. Sanitized HTML.</summary>
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public List<WordTranslation> Translations { get; set; } = [];
    public List<WordTag> WordTags { get; set; } = [];
    public List<SrsState> SrsStates { get; set; } = [];
}
