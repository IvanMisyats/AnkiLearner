using System.ComponentModel.DataAnnotations;

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
    [MaxLength(200)] string? Transcription,
    [MaxLength(50)] string? PartOfSpeech,
    [MaxLength(30)] string? Gender,
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
    [Range(0, 1000)] int DailyNewLimit);
