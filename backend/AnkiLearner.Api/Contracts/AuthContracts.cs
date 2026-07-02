using System.ComponentModel.DataAnnotations;

namespace AnkiLearner.Api.Contracts;

// Note: validation attributes must target the record constructor parameters
// (MVC ignores [property:]-targeted metadata on positional records).
public record RegisterRequest(
    [Required, EmailAddress] string Email,
    [Required, MinLength(8)] string Password);

public record LoginRequest(
    [Required, EmailAddress] string Email,
    [Required] string Password);

public record UserDto(Guid Id, string Email);

public record SettingsDto(string LearningLanguage, List<string> KnownLanguages, int DailyNewLimit);

public record AuthResponse(string AccessToken, UserDto User);

public record MeResponse(UserDto User, SettingsDto Settings);
