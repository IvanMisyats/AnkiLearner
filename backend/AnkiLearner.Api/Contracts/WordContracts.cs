using System.ComponentModel.DataAnnotations;
using AnkiLearner.Core.Entities;

namespace AnkiLearner.Api.Contracts;

public record TranslationDto(
    [Required, MaxLength(20)] string LanguageCode,
    [Required] string Text,
    string? ExampleTranslation);

public record WordDto(
    Guid Id,
    string LanguageCode,
    string Term,
    string? Transcription,
    string? PartOfSpeech,
    string? Gender,
    string? Example,
    string? Notes,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    List<TranslationDto> Translations,
    List<string> Tags);

public record SaveWordRequest(
    [Required] string Term,
    [MaxLength(Word.TranscriptionMaxLength)] string? Transcription,
    [MaxLength(Word.PartOfSpeechMaxLength)] string? PartOfSpeech,
    [MaxLength(Word.GenderMaxLength)] string? Gender,
    string? Example,
    string? Notes,
    List<TranslationDto>? Translations,
    List<string>? Tags);

public record PagedResponse<T>(List<T> Items, int Total, int Page, int PageSize);

public record DuplicateCheckResponse(bool Exists, Guid? WordId);

public record TagDto(Guid Id, string Name, int Count);

public record SaveTagRequest([Required, MaxLength(100)] string Name);

public record UpdateSettingsRequest(
    [Required, MaxLength(20)] string LearningLanguage,
    [Required, MinLength(1)] List<string> KnownLanguages,
    [Range(0, 1000)] int DailyNewLimit,
    [Range(1, 36500)] int? MaxIntervalDays = null);
