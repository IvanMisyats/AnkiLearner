using AnkiLearner.Api.Contracts;
using AnkiLearner.Api.Services;
using AnkiLearner.Core;
using AnkiLearner.Core.Abstractions;
using AnkiLearner.Core.Srs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AnkiLearner.Api.Controllers;

[ApiController]
[Route("api/settings")]
[Authorize]
public class SettingsController(
    Infrastructure.Data.AppDbContext db,
    ICurrentUser currentUser,
    SettingsService settingsService,
    FsrsInitializer fsrsInitializer) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<SettingsDto>> Get(CancellationToken ct)
    {
        var settings = await settingsService.GetOrCreateAsync(currentUser.UserId, ct);
        return ToDto(settings);
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
        if (request.FsrsWeights is { Length: > 0 } weights && Fsrs.Validate(weights) is { } weightError)
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: weightError);

        var settings = await settingsService.GetOrCreateAsync(currentUser.UserId, ct);
        var before = settings.SchedulerParameters();
        settings.LearningLanguage = request.LearningLanguage;
        settings.KnownLanguages = known;
        settings.DailyNewLimit = request.DailyNewLimit;
        if (request.MaxIntervalDays is { } max) settings.MaxIntervalDays = max;
        if (request.DesiredRetention is { } retention) settings.DesiredRetention = retention;
        // Omitted = unchanged; an empty list goes back to the default weights.
        if (request.FsrsWeights is { } newWeights)
            settings.FsrsWeights = newWeights.Length == 0 ? null : newWeights;
        settings.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        // New weights, retention or cap: redo the setup of every card not yet studied under FSRS,
        // so pasting weights and importing history can happen in either order (spec §5).
        if (!SameScheduler(before, settings.SchedulerParameters()))
            await fsrsInitializer.TrySetUpAsync(currentUser.UserId, onlyUnset: false);

        return ToDto(settings);
    }

    private static SettingsDto ToDto(Core.Entities.UserSettings s) => SettingsDto.From(s);

    private static bool SameScheduler(FsrsParameters a, FsrsParameters b) =>
        a.DesiredRetention == b.DesiredRetention && a.MaxIntervalDays == b.MaxIntervalDays
        && a.Weights.SequenceEqual(b.Weights);
}

[ApiController]
[Route("api/languages")]
public class LanguagesController : ControllerBase
{
    /// <summary>Static catalog (spec FR-S5). Anonymous: it is not user data.</summary>
    [HttpGet]
    public ActionResult<IReadOnlyList<Language>> List() => Ok(LanguageCatalog.All);
}
