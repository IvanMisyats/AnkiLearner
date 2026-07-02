using AnkiLearner.Api.Contracts;
using AnkiLearner.Api.Services;
using AnkiLearner.Core;
using AnkiLearner.Core.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AnkiLearner.Api.Controllers;

[ApiController]
[Route("api/settings")]
[Authorize]
public class SettingsController(
    Infrastructure.Data.AppDbContext db,
    ICurrentUser currentUser,
    SettingsService settingsService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<SettingsDto>> Get(CancellationToken ct)
    {
        var settings = await settingsService.GetOrCreateAsync(currentUser.UserId, ct);
        return new SettingsDto(settings.LearningLanguage, settings.KnownLanguages, settings.DailyNewLimit);
    }

    [HttpPut]
    public async Task<ActionResult<SettingsDto>> Update(UpdateSettingsRequest request, CancellationToken ct)
    {
        if (!LanguageCatalog.IsValid(request.LearningLanguage))
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                title: $"Unknown language code '{request.LearningLanguage}'.");

        var known = request.KnownLanguages.Distinct().ToList();
        var invalid = known.FirstOrDefault(c => !LanguageCatalog.IsValid(c));
        if (invalid is not null)
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                title: $"Unknown language code '{invalid}'.");
        if (known.Contains(request.LearningLanguage))
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "The learning language cannot also be a known language.");

        var settings = await settingsService.GetOrCreateAsync(currentUser.UserId, ct);
        settings.LearningLanguage = request.LearningLanguage;
        settings.KnownLanguages = known;
        settings.DailyNewLimit = request.DailyNewLimit;
        settings.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return new SettingsDto(settings.LearningLanguage, settings.KnownLanguages, settings.DailyNewLimit);
    }
}

[ApiController]
[Route("api/languages")]
public class LanguagesController : ControllerBase
{
    /// <summary>Static catalog (spec FR-S5). Anonymous: it is not user data.</summary>
    [HttpGet]
    public ActionResult<IReadOnlyList<Language>> List() => Ok(LanguageCatalog.All);
}
