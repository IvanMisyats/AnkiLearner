using AnkiLearner.Api.Services;
using AnkiLearner.Core;
using AnkiLearner.Core.Abstractions;
using AnkiLearner.Core.Entities;
using AnkiLearner.Core.Srs;
using AnkiLearner.Infrastructure.Anki;
using AnkiLearner.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace AnkiLearner.Api.Controllers;

public record ImportPreviewResponse(
    string ImportId, int Total, int New, int Duplicates, int WithProgress,
    int ExistingWithHistory, // words already in the dictionary that the file has review history for
    List<string> Skipped);

/// <param name="ImportHistory">Bring each imported card's Anki review history along (FR-I9).
/// Needs <paramref name="ImportProgress"/>: history goes only to cards that get a state.</param>
/// <param name="HistoryOnly">Add no words; only give existing words their Anki review history.</param>
public record ImportCommitRequest(
    bool ImportDuplicates, bool ImportProgress, bool ImportHistory = false, bool HistoryOnly = false);

public record ImportCommitResponse(int Imported, int StatesImported, int HistoryCards = 0, int HistoryReviews = 0);

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
    IConfiguration config,
    FsrsInitializer fsrsInitializer) : ControllerBase
{
    private static readonly TimeSpan StagingLifetime = TimeSpan.FromMinutes(30);

    /// <summary>Uploads being committed right now. TryAdd is atomic, so of two simultaneous
    /// commits of one upload (a double click) only one gets to run.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> Committing = new();

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
        var existing = await ExistingNormalizedTermsAsync(ct);
        var seen = new HashSet<string>(existing);
        var duplicates = 0;
        foreach (var note in result.Notes)
        {
            var normalized = TermNormalizer.Normalize(note.Front);
            if (!seen.Add(normalized)) duplicates++;
        }
        var settings = await settingsService.GetOrCreateAsync(currentUser.UserId, ct);
        var existingWithHistory = (await HistoryMatchesAsync(result, settings, ct))
            .Select(m => m.WordId).Distinct().Count();

        return new ImportPreviewResponse(
            importId,
            result.Notes.Count,
            result.Notes.Count - duplicates,
            duplicates,
            result.Notes.Count(HasImportableProgress),
            existingWithHistory,
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
        // Claim it now, so a double-clicked commit can't import the same file twice.
        if (!Committing.TryAdd(importId, 0))
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "This import is already running.");
        try
        {
            cache.Remove($"import:{importId}");
            return await CommitStagedAsync(staged, request, ct);
        }
        finally
        {
            Committing.TryRemove(importId, out _);
        }
    }

    private async Task<ActionResult<ImportCommitResponse>> CommitStagedAsync(
        StagedImport staged, ImportCommitRequest request, CancellationToken ct)
    {

        var settings = await settingsService.GetOrCreateAsync(currentUser.UserId, ct);
        if (request.HistoryOnly)
            return await CommitHistoryOnlyAsync(staged.Result, settings, ct);

        var primaryLanguage = settings.KnownLanguages.FirstOrDefault() ?? "en";
        var existing = await ExistingNormalizedTermsAsync(ct);
        var tagCache = await db.Tags
            .Where(t => t.UserId == currentUser.UserId)
            .ToDictionaryAsync(t => t.Name, t => t, StringComparer.OrdinalIgnoreCase, ct);
        var now = DateTime.UtcNow;
        var crt = staged.Result.CollectionCreatedUtc;

        var imported = 0;
        var statesImported = 0;
        var historyCards = 0;
        var historyReviews = 0;
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
            {
                statesImported += AddStates(word, note, crt);
                // History goes only to cards that got a state (a review card in Anki).
                if (request.ImportHistory)
                    foreach (var state in word.SrsStates)
                    {
                        var card = note.Cards.FirstOrDefault(c => IsImportableReviewCard(c) && Exercise(c) == state.Exercise);
                        if (card is null || card.Reviews.Count == 0) continue;
                        foreach (var row in HistoryRows(card, state.Exercise))
                        {
                            row.Word = word;
                            db.ReviewLog.Add(row);
                        }
                        historyCards++;
                        historyReviews += card.Reviews.Count;
                    }
            }

            db.Words.Add(word);
            imported++;
        }

        if (await SaveOrConflictAsync("Some of these words or tags were just added by another import.", ct) is { } conflict)
            return conflict; // one transaction
        // Imported progress is SM-2 state; give it FSRS memory and reschedule it (spec §5).
        if (statesImported > 0)
            await fsrsInitializer.TrySetUpAsync(currentUser.UserId, onlyUnset: true);
        return new ImportCommitResponse(imported, statesImported, historyCards, historyReviews);
    }

    /// <summary>
    /// "History only" (FR-I9): adds no words — a re-upload must not bring back words deleted since
    /// the first import. Gives each existing word's directions the review history of its Anki card,
    /// then sets FSRS up again from it.
    /// </summary>
    private async Task<ActionResult<ImportCommitResponse>> CommitHistoryOnlyAsync(
        ApkgParseResult result, UserSettings settings, CancellationToken ct)
    {
        var historyCards = 0;
        var historyReviews = 0;
        foreach (var (wordId, exercise, card) in await HistoryMatchesAsync(result, settings, ct))
        {
            foreach (var row in HistoryRows(card, exercise))
            {
                row.WordId = wordId;
                db.ReviewLog.Add(row);
            }
            historyCards++;
            historyReviews += card.Reviews.Count;
        }

        if (await SaveOrConflictAsync("This history is already imported.", ct) is { } conflict) return conflict;
        if (historyCards > 0)
            await fsrsInitializer.TrySetUpAsync(currentUser.UserId, onlyUnset: false);
        return new ImportCommitResponse(0, 0, historyCards, historyReviews);
    }

    /// <summary>Saves; a unique-index violation (something already stored) becomes a 409.</summary>
    private async Task<ActionResult?> SaveOrConflictAsync(string conflictMessage, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return null;
        }
        catch (DbUpdateException e) when (DbErrors.IsUniqueViolation(e))
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: conflictMessage);
        }
    }

    /// <summary>
    /// Which existing word directions would get which Anki card's history in "History only"
    /// (FR-I9) — the preview counts the same thing. Only directions that have a state and no Anki
    /// history yet; a term shared by several words is ambiguous and skipped.
    /// </summary>
    private async Task<List<(Guid WordId, ExerciseType Exercise, ApkgCard Card)>> HistoryMatchesAsync(
        ApkgParseResult result, UserSettings settings, CancellationToken ct)
    {
        // Existing words by normalized term; a term shared by several words is ambiguous — skipped.
        var words = (await db.Words
                .Where(w => w.UserId == currentUser.UserId && w.LanguageCode == settings.LearningLanguage)
                .Select(w => new { w.Id, w.TermNormalized })
                .ToListAsync(ct))
            .GroupBy(w => w.TermNormalized)
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.Single().Id);
        var wordIds = words.Values.ToList();
        var states = await db.SrsStates.AsNoTracking()
            .Where(s => wordIds.Contains(s.WordId))
            .ToDictionaryAsync(s => (s.WordId, s.Exercise), ct);
        var alreadyHasHistory = (await db.ReviewLog
                .Where(r => r.Source == ReviewSource.Anki && wordIds.Contains(r.WordId))
                .Select(r => new { r.WordId, r.Exercise })
                .Distinct()
                .ToListAsync(ct))
            .Select(r => (r.WordId, r.Exercise))
            .ToHashSet();

        if (states.Count == 0) return [];

        // Every card of a note whose term is a known word, grouped by word and direction - also
        // cards without reviews: the note the first import kept may be one of those. Each note is
        // sanitized once.
        var candidates = result.Notes
            .Select(n => (Note: n, Term: TermNormalizer.Normalize(sanitizer.Sanitize(n.Front))))
            .Where(x => words.ContainsKey(x.Term))
            .SelectMany(x => x.Note.Cards.Where(c => c.Ord is 0 or 1).Select(c => (x.Term, Card: c)))
            .GroupBy(x => (WordId: words[x.Term], Exercise: Exercise(x.Card)));

        var matches = new List<(Guid, ExerciseType, ApkgCard)>();
        foreach (var group in candidates)
        {
            var key = group.Key;
            if (!states.TryGetValue((key.WordId, key.Exercise), out var state)) continue; // never studied here
            if (alreadyHasHistory.Contains((key.WordId, key.Exercise))) continue;        // re-upload
            if (PickCard(group.Select(x => x.Card).ToList(), state) is { Reviews.Count: > 0 } card)
                matches.Add((key.WordId, key.Exercise, card));
        }
        return matches;
    }

    /// <summary>
    /// One Anki card per word and direction: several notes can share a term (the first import kept
    /// one word). Prefer the card whose ease and repetitions the state still carries from that
    /// import — FSRS never changes them (lapses it does). No unique pick → no history (the
    /// snapshot is used).
    /// </summary>
    private static ApkgCard? PickCard(List<ApkgCard> cards, SrsState state)
    {
        if (cards.Count == 1) return cards[0];
        var matching = cards.Where(c =>
                Math.Abs(ImportedEase(c) - state.EaseFactor) < 1e-9 &&
                Math.Max(2, c.Reps) == state.Repetitions)
            .ToList();
        return matching.Count == 1 ? matching[0] : null;
    }

    /// <summary>Review-log rows for an Anki card's history, oldest first (FR-I9).</summary>
    private static IEnumerable<ReviewLogEntry> HistoryRows(ApkgCard card, ExerciseType exercise)
    {
        DateTime? previous = null;
        foreach (var review in card.Reviews)
        {
            var at = DateTimeOffset.FromUnixTimeMilliseconds(review.Id).UtcDateTime;
            yield return new ReviewLogEntry
            {
                Exercise = exercise,
                Kind = ReviewKind.Review,
                Source = ReviewSource.Anki,
                Grade = (ReviewGrade)(review.Ease - 1), // Anki 1–4 → Again..Easy
                ReviewedAt = at,
                PreviousReviewAt = previous,
                // Positive = days; negative = seconds while learning, which has no day interval.
                IntervalBefore = review.LastIntervalDays > 0 ? review.LastIntervalDays : null,
                IntervalAfter = review.IntervalDays > 0 ? review.IntervalDays : null,
                EaseAfter = review.Factor > 0 ? review.Factor / 1000.0 : null,
                AnkiRevlogId = review.Id,
            };
            previous = at;
        }
    }

    private static ExerciseType Exercise(ApkgCard card) =>
        card.Ord == 0 ? ExerciseType.TargetToKnown : ExerciseType.KnownToTarget;

    private static double ImportedEase(ApkgCard card) =>
        card.EaseFactor == 0 ? 2.5 : Math.Max(1.3, card.EaseFactor / 1000.0);

    /// <summary>Maps Anki per-card scheduling onto SM-2 state (spec FR-I7, best effort).</summary>
    private static int AddStates(Word word, ApkgNote note, DateTime collectionCreated)
    {
        var added = 0;
        foreach (var card in note.Cards)
        {
            if (!IsImportableReviewCard(card)) continue;
            var exercise = Exercise(card);
            if (word.SrsStates.Any(s => s.Exercise == exercise)) continue; // unique per direction

            word.SrsStates.Add(new SrsState
            {
                Exercise = exercise,
                IntervalDays = card.IntervalDays,
                // factor is permille; 0 means "never reviewed" — fall back to the SM-2 start.
                EaseFactor = ImportedEase(card),
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
