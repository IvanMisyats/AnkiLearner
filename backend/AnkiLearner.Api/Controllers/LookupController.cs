using System.ComponentModel.DataAnnotations;
using AnkiLearner.Api.Services;
using AnkiLearner.Core.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AnkiLearner.Api.Controllers;

public record LookupRequest([Required, MaxLength(200)] string Term);

public record LookupStatusResponse(bool Available, string Provider);

[ApiController]
[Route("api/lookup")]
[Authorize]
public class LookupController(
    IWordLookupProvider provider,
    ICurrentUser currentUser,
    SettingsService settingsService,
    ILogger<LookupController> logger) : ControllerBase
{
    [HttpGet("status")]
    public ActionResult<LookupStatusResponse> Status() =>
        new LookupStatusResponse(provider.IsAvailable, provider.Name);

    /// <summary>Looks up a term without persisting anything (spec FR-W4).</summary>
    [HttpPost]
    [EnableRateLimiting("lookup")]
    public async Task<ActionResult<WordLookupResult>> Lookup(LookupRequest request, CancellationToken ct)
    {
        if (!provider.IsAvailable)
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "AI lookup is not configured on this server.");

        var settings = await settingsService.GetOrCreateAsync(currentUser.UserId, ct);
        try
        {
            return await provider.LookupAsync(
                request.Term, settings.LearningLanguage, settings.KnownLanguages, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Word lookup failed for term of length {Length}", request.Term.Length);
            return Problem(statusCode: StatusCodes.Status502BadGateway,
                title: "The lookup provider failed. You can still fill the form manually.");
        }
    }
}
