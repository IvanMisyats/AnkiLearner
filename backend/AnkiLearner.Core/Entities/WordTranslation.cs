namespace AnkiLearner.Core.Entities;

/// <summary>A word's translation in one of the user's known languages. Unique per (WordId, LanguageCode).</summary>
public class WordTranslation
{
    public Guid Id { get; set; }
    public Guid WordId { get; set; }
    public Word Word { get; set; } = null!;

    /// <summary>BCP-47 code of the known language, e.g. "en", "uk".</summary>
    public string LanguageCode { get; set; } = string.Empty;

    /// <summary>Meanings — may be an HTML list. Sanitized HTML.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Translation of the word's example sentence. Sanitized HTML.</summary>
    public string? ExampleTranslation { get; set; }
}
