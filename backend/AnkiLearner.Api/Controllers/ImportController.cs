using AnkiLearner.Api.Services;
using AnkiLearner.Core;
using AnkiLearner.Core.Abstractions;
using AnkiLearner.Core.Entities;
using AnkiLearner.Infrastructure.Anki;
using AnkiLearner.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace AnkiLearner.Api.Controllers;

public record ImportPreviewResponse(
    string ImportId, int Total, int New, int Duplicates, int WithProgress, List<string> Skipped);

public record ImportCommitRequest(bool ImportDuplicates, bool ImportProgress);

public record ImportCommitResponse(int Imported, int StatesImported);

/// <summary>Parsed upload staged between the preview and commit steps (spec FR-I3).</summary>
internal sealed record StagedImport(Guid UserId, ApkgParseResult Result);

[ApiController]
[Route("api/import")]
[Authorize]
public class ImportController(
    AppDbContext db,
    ICurrentUser currentUser,
    SettingsService settingsService,
    IContentSanitizer sanitizer,
    IMemoryCache cache,
    IConfiguration config) : ControllerBase
{
    private static readonly TimeSpan StagingLifetime = TimeSpan.FromMinutes(30);

    [HttpPost("apkg")]
    public async Task<ActionResult<ImportPreviewResponse>> Upload(IFormFile file, CancellationToken ct)
    {
        var maxBytes = config.GetValue("Import:MaxUploadBytes", 52_428_800L);
        if (file is null || file.Length == 0 || file.Length > maxBytes)
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                title: $"The file must be between 1 byte and {maxBytes / (1024 * 1024)} MB.");

        ApkgParseResult result;
        try
        {
            await using var stream = file.OpenReadStream();
            result = ApkgParser.Parse(stream);
        }
        catch (ApkgFormatException ex)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message);
        }
        catch (Exception)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "The file could not be read as an Anki package.");
        }

        var importId = Guid.NewGuid().ToString("N");
        cache.Set($"import:{importId}", new StagedImport(currentUser.UserId, result), StagingLifetime);

        // Count duplicates against the dictionary AND within the file itself, so the
        // preview matches what a default commit will actually import.
        var seen = await ExistingNormalizedTermsAsync(ct);
        var duplicates = 0;
        foreach (var note in result.Notes)
        {
            var normalized = TermNormalizer.Normalize(note.Front);
            if (!seen.Add(normalized)) duplicates++;
        }

        return new ImportPreviewResponse(
            importId,
            result.Notes.Count,
            result.Notes.Count - duplicates,
            duplicates,
            result.Notes.Count(HasImportableProgress),
            result.Skipped);
    }

    [HttpPost("apkg/{importId}/commit")]
    public async Task<ActionResult<ImportCommitResponse>> Commit(
        string importId, ImportCommitRequest request, CancellationToken ct)
    {
        if (!cache.TryGetValue($"import:{importId}", out StagedImport? staged) ||
            staged is null || staged.UserId != currentUser.UserId)
            return Problem(statusCode: StatusCodes.Status404NotFound,
                title: "This import has expired — upload the file again.");

        var settings = await settingsService.GetOrCreateAsync(currentUser.UserId, ct);
        var primaryLanguage = settings.KnownLanguages.FirstOrDefault() ?? "en";
        var existing = await ExistingNormalizedTermsAsync(ct);
        var tagCache = await db.Tags
            .Where(t => t.UserId == currentUser.UserId)
            .ToDictionaryAsync(t => t.Name, t => t, StringComparer.OrdinalIgnoreCase, ct);
        var now = DateTime.UtcNow;
        var crt = staged.Result.CollectionCreatedUtc;

        var imported = 0;
        var statesImported = 0;
        foreach (var note in staged.Result.Notes)
        {
            var term = sanitizer.Sanitize(note.Front);
            var normalized = TermNormalizer.Normalize(term);
            if (normalized.Length == 0) continue;
            if (existing.Contains(normalized) && !request.ImportDuplicates) continue;
            existing.Add(normalized);

            var word = new Word
            {
                UserId = currentUser.UserId,
                LanguageCode = settings.LearningLanguage,
                Term = term,
                TermNormalized = normalized,
                CreatedAt = now,
                UpdatedAt = now,
            };
            word.Translations.Add(new WordTranslation
            {
                LanguageCode = primaryLanguage,
                Text = sanitizer.Sanitize(note.Back),
            });

            foreach (var tagName in TagNamesFor(note))
                word.WordTags.Add(new WordTag { Tag = GetOrCreateTag(tagCache, tagName) });

            if (request.ImportProgress)
                statesImported += AddStates(word, note, crt);

            db.Words.Add(word);
            imported++;
        }

        await db.SaveChangesAsync(ct); // one SaveChanges = one transaction
        cache.Remove($"import:{importId}");
        return new ImportCommitResponse(imported, statesImported);
    }

    /// <summary>Maps Anki per-card scheduling onto SM-2 state (spec FR-I7, best effort).</summary>
    private static int AddStates(Word word, ApkgNote note, DateTime collectionCreated)
    {
        var added = 0;
        foreach (var card in note.Cards)
        {
            if (!IsImportableReviewCard(card)) continue;
            var exercise = card.Ord == 0 ? ExerciseType.TargetToKnown : ExerciseType.KnownToTarget;
            if (word.SrsStates.Any(s => s.Exercise == exercise)) continue; // unique per direction

            word.SrsStates.Add(new SrsState
            {
                Exercise = exercise,
                IntervalDays = card.IntervalDays,
                // factor is permille; 0 means "never reviewed" — fall back to the SM-2 start.
                EaseFactor = card.EaseFactor == 0 ? 2.5 : Math.Max(1.3, card.EaseFactor / 1000.0),
                Lapses = card.Lapses,
                // ≥2 so the next successful review multiplies the interval instead of
                // restarting the 1d/6d ladder (SM-2 only distinguishes 1, 2, ≥3).
                Repetitions = Math.Max(2, card.Reps),
                // For review cards `due` is days since collection creation; past = due now.
                Due = collectionCreated.AddDays(card.Due),
                // Dated at collection creation so imports never eat today's new-card allowance.
                CreatedAt = collectionCreated,
            });
            added++;
        }
        return added;
    }

    private static bool HasImportableProgress(ApkgNote note) => note.Cards.Any(IsImportableReviewCard);

    private static bool IsImportableReviewCard(ApkgCard card) =>
        card.Type == 2 &&          // review card (new/learning start fresh)
        card.Queue != -1 &&        // not suspended
        card.IntervalDays > 0 &&   // negative ivl = seconds (learning phase)
        card.Ord is 0 or 1;        // front→back / back→front

    private static IEnumerable<string> TagNamesFor(ApkgNote note)
    {
        var names = new List<string> { "imported" };
        if (!string.IsNullOrWhiteSpace(note.DeckName)) names.Add(note.DeckName);
        names.AddRange(note.Tags);
        return names.Select(n => n.Trim()).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private Tag GetOrCreateTag(Dictionary<string, Tag> tagCache, string name)
    {
        if (tagCache.TryGetValue(name, out var tag)) return tag;
        tag = new Tag { UserId = currentUser.UserId, Name = name };
        db.Tags.Add(tag);
        tagCache[name] = tag;
        return tag;
    }

    private async Task<HashSet<string>> ExistingNormalizedTermsAsync(CancellationToken ct)
    {
        var settings = await settingsService.GetOrCreateAsync(currentUser.UserId, ct);
        var terms = await db.Words
            .Where(w => w.UserId == currentUser.UserId && w.LanguageCode == settings.LearningLanguage)
            .Select(w => w.TermNormalized)
            .ToListAsync(ct);
        return [.. terms];
    }
}
