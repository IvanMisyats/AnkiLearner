using System.ComponentModel.DataAnnotations;
using AnkiLearner.Core.Abstractions;

namespace AnkiLearner.Api.Contracts;

/// <summary>POST /api/translate. <c>SourceLanguage</c> null/empty = auto-detect. <c>Text</c> may
/// be up to 1000 raw characters (a paste with line breaks); after cleaning it must fit
/// <see cref="Core.TranslateShaper.MaxTextLength"/> (300).</summary>
public record TranslateRequest(
    [Required, MaxLength(1000)] string Text,
    [MaxLength(20)] string? SourceLanguage);

/// <summary>A suggested learning-language entry. <c>ExistingWordId</c> is set when the user's
/// dictionary already holds it (leading article/particle ignored, spec FR-W7).</summary>
public record TranslateCardDto(
    string Term,
    string Transcription,
    string PartOfSpeech,
    string Gender,
    Dictionary<string, List<string>> Meanings,
    string Example,
    Dictionary<string, string> ExampleTranslations,
    string Note,
    Guid? ExistingWordId);

/// <summary><c>InputKind</c> serializes as "Word" / "Phrase" / "Sentence". <c>WholeInput</c> is
/// the whole multi-word input as one entry, when no card already is.</summary>
public record TranslateResponse(
    string SourceLanguage,
    InputKind InputKind,
    Dictionary<string, string> InputTranslations,
    List<TranslateCardDto> Cards,
    TranslateCardDto? WholeInput);
