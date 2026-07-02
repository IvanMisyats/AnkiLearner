using AnkiLearner.Core.Entities;

namespace AnkiLearner.Api.Contracts;

public static class WordMapper
{
    /// <summary>Requires Translations and WordTags(.Tag) to be loaded.</summary>
    public static WordDto ToDto(Word w) => new(
        w.Id,
        w.LanguageCode,
        w.Term,
        w.Transcription,
        w.PartOfSpeech,
        w.Gender,
        w.Example,
        w.Notes,
        w.CreatedAt,
        w.UpdatedAt,
        w.Translations
            .OrderBy(t => t.LanguageCode)
            .Select(t => new TranslationDto(t.LanguageCode, t.Text, t.ExampleTranslation))
            .ToList(),
        w.WordTags.Select(wt => wt.Tag.Name).OrderBy(n => n).ToList());
}
