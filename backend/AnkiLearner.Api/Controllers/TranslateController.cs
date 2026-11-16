using AnkiLearner.Api.Contracts;
using AnkiLearner.Api.Services;
using AnkiLearner.Core;
using AnkiLearner.Core.Abstractions;
using AnkiLearner.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace AnkiLearner.Api.Controllers;

[ApiController]
[Route("api/translate")]
[Authorize]
public class TranslateController(
    AppDbContext db,
    IWordLookupProvider provider,
    ICurrentUser currentUser,
    SettingsService settingsService,
    ILogger<TranslateController> logger) : ControllerBase
{
    /// <summary>Translates a word, phrase or sentence and suggests learning-language cards
    /// (spec §3.8). Persists nothing: saving a card is the client's own POST /api/words.</summary>
    [HttpPost]
    [EnableRateLimiting("lookup")] // one per-user AI budget, shared with the lookup
    public async Task<ActionResult<TranslateResponse>> Translate(TranslateRequest request, CancellationToken ct)
    {
        if (!provider.IsAvailable)
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "AI translation is not configured on this server.");

        var settings = await settingsService.GetOrCreateAsync(currentUser.UserId, ct);
        var learning = settings.LearningLanguage;
        var known = settings.KnownLanguages;

        var text = TranslateShaper.CleanText(request.Text);
        if (text.Length == 0)
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Enter a word, phrase or sentence to translate.");
        if (text.Length > TranslateShaper.MaxTextLength)
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                title: $"Translate at most {TranslateShaper.MaxTextLength} characters (one sentence).");

        // Empty = auto-detect; otherwise it must be one of the user's own languages ("EN" = "en").
        var source = string.IsNullOrWhiteSpace(request.SourceLanguage)
            ? null
            : request.SourceLanguage.Trim().ToLowerInvariant();
        if (source is not null && source != learning && !known.Contains(source))
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                title: $"'{source}' is neither your learning language nor one of your known languages.");

        TranslateResult raw;
        try
        {
            raw = await provider.TranslateAsync(text, source, learning, known, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // the client went away
        }
        catch (Exception ex)
        {
            // Timeouts land here too. The provider's own exceptions carry neither the user's text
            // nor the model's output; an SDK exception may include the API's error body.
            logger.LogError(ex, "Translation failed for text of length {Length}", text.Length);
            return Problem(statusCode: StatusCodes.Status502BadGateway,
                title: "The translation provider failed.");
        }

        var (result, wholeInput) = TranslateShaper.Shape(raw, text, source, learning, known);
        if (result.SourceLanguage == TranslateShaper.OtherLanguage)
            return Problem(statusCode: StatusCodes.Status422UnprocessableEntity,
                title: $"Only {LanguageList(learning, known)} are supported.");

        // One entry per card, plus the whole input, which is also looked up without trailing
        // punctuation: "Tak for mad!" finds a saved "tak for mad".
        var entries = result.Cards.Select(c => new[] { c.Term }).ToList();
        if (wholeInput is not null)
            entries.Add([wholeInput.Term, TranslateShaper.TrimTrailingPunctuation(wholeInput.Term)]);
        var existingIds = await FindExistingWordsAsync(entries, learning, ct);

        TranslateCardDto ToDto(TranslateCard c, Guid? existingWordId) => new(
            c.Term, c.Transcription, c.PartOfSpeech, c.Gender, c.Meanings,
            c.Example, c.ExampleTranslations, c.Note, existingWordId);

        return new TranslateResponse(
            result.SourceLanguage,
            result.InputKind,
            result.InputTranslations,
            result.Cards.Select((c, i) => ToDto(c, existingIds[i])).ToList(),
            wholeInput is null ? null : ToDto(wholeInput, existingIds[^1]));
    }

    /// <summary>For each entry (one or more spellings of it), the user's matching word in the
    /// current learning language, with ONE query over the (UserId, LanguageCode, TermNormalized)
    /// index. Same rule as the duplicate check (FR-W7): a leading particle is ignored, an exact
    /// match wins, then the oldest word, then the lowest id. A word that exactly matches one entry
    /// is never a looser match for another: with only "en gift" (poison) saved, the card "gift"
    /// (married) stays unflagged, so the user can add it.</summary>
    private async Task<Guid?[]> FindExistingWordsAsync(
        List<string[]> entries, string learning, CancellationToken ct)
    {
        var candidates = entries
            .Select(forms => forms.SelectMany(f => TermNormalizer.MatchCandidates(f, learning)).ToHashSet())
            .ToList();
        var exact = entries
            .Select(forms => forms.Select(TermNormalizer.Normalize).ToHashSet())
            .ToList();

        var allCandidates = candidates.SelectMany(c => c).Distinct().ToList();
        if (allCandidates.Count == 0) return new Guid?[entries.Count];

        var userId = currentUser.UserId;
        var words = await db.Words
            .Where(w => w.UserId == userId && w.LanguageCode == learning && allCandidates.Contains(w.TermNormalized))
            .Select(w => new { w.Id, w.TermNormalized, w.CreatedAt })
            .ToListAsync(ct);

        var takenExactly = words
            .Where(w => exact.Any(e => e.Contains(w.TermNormalized)))
            .Select(w => w.Id)
            .ToHashSet();

        return entries.Select((_, i) => words
                .Where(w => candidates[i].Contains(w.TermNormalized)
                    && (exact[i].Contains(w.TermNormalized) || !takenExactly.Contains(w.Id)))
                .OrderBy(w => exact[i].Contains(w.TermNormalized) ? 0 : 1)
                .ThenBy(w => w.CreatedAt)
                .ThenBy(w => w.Id) // imported words share one CreatedAt
                .Select(w => (Guid?)w.Id)
                .FirstOrDefault())
            .ToArray();
    }

    /// <summary>"Danish, English and Ukrainian".</summary>
    private static string LanguageList(string learning, IReadOnlyList<string> known)
    {
        var names = known.Prepend(learning).Select(LanguageCatalog.DisplayName).ToList();
        return names.Count == 1
            ? names[0]
            : $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}";
    }
}
