using AnkiLearner.Api.Contracts;
using AnkiLearner.Api.Services;
using AnkiLearner.Core;
using AnkiLearner.Core.Abstractions;
using AnkiLearner.Core.Entities;
using AnkiLearner.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AnkiLearner.Api.Controllers;

[ApiController]
[Route("api/words")]
[Authorize]
public class WordsController(
    AppDbContext db,
    ICurrentUser currentUser,
    SettingsService settingsService,
    IContentSanitizer sanitizer) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResponse<WordDto>>> List(
        [FromQuery] string? search,
        [FromQuery] string? tag,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = await ScopedWordsAsync(ct);

        if (!string.IsNullOrWhiteSpace(search))
        {
            // Escape ILIKE metacharacters so "100%" doesn't match everything.
            var escaped = search.Trim()
                .Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");
            var pattern = $"%{escaped}%";
            query = query.Where(w =>
                EF.Functions.ILike(w.Term, pattern) ||
                w.Translations.Any(t => EF.Functions.ILike(t.Text, pattern)));
        }

        if (!string.IsNullOrWhiteSpace(tag))
        {
            // Comma-separated tag names combine with OR (spec FR-D1).
            var names = tag.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            query = query.Where(w => w.WordTags.Any(wt => names.Contains(wt.Tag.Name)));
        }

        var total = await query.CountAsync(ct);
        var words = await query
            .OrderByDescending(w => w.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(w => w.Translations)
            .Include(w => w.WordTags).ThenInclude(wt => wt.Tag)
            .AsNoTracking()
            .ToListAsync(ct);

        return new PagedResponse<WordDto>(words.Select(ToDto).ToList(), total, page, pageSize);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<WordDto>> Get(Guid id, CancellationToken ct)
    {
        var word = await LoadOwnedWordAsync(id, track: false, ct);
        return word is null ? NotFound() : ToDto(word);
    }

    /// <summary>Non-blocking duplicate check for the add/edit form (spec FR-W7).</summary>
    [HttpGet("duplicate")]
    public async Task<ActionResult<DuplicateCheckResponse>> CheckDuplicate(
        [FromQuery] string term, [FromQuery] Guid? excludeId, CancellationToken ct)
    {
        var normalized = TermNormalizer.Normalize(term);
        if (normalized.Length == 0) return new DuplicateCheckResponse(false, null);

        var query = (await ScopedWordsAsync(ct)).Where(w => w.TermNormalized == normalized);
        if (excludeId is not null)
            query = query.Where(w => w.Id != excludeId);

        var match = await query.Select(w => (Guid?)w.Id).FirstOrDefaultAsync(ct);
        return new DuplicateCheckResponse(match is not null, match);
    }

    [HttpPost]
    public async Task<ActionResult<WordDto>> Create(SaveWordRequest request, CancellationToken ct)
    {
        var settings = await settingsService.GetOrCreateAsync(currentUser.UserId, ct);
        var error = ValidateTranslations(request, settings.KnownLanguages);
        if (error is not null) return error;

        var word = new Word
        {
            UserId = currentUser.UserId,
            LanguageCode = settings.LearningLanguage,
            CreatedAt = DateTime.UtcNow,
        };
        await ApplyAsync(word, request, settings.KnownLanguages, ct);
        if (word.TermNormalized.Length == 0)
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "The term is empty after sanitization.");
        db.Words.Add(word);
        await db.SaveChangesAsync(ct);

        var created = await LoadOwnedWordAsync(word.Id, track: false, ct);
        return CreatedAtAction(nameof(Get), new { id = word.Id }, ToDto(created!));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<WordDto>> Update(Guid id, SaveWordRequest request, CancellationToken ct)
    {
        var settings = await settingsService.GetOrCreateAsync(currentUser.UserId, ct);
        var error = ValidateTranslations(request, settings.KnownLanguages);
        if (error is not null) return error;

        var word = await LoadOwnedWordAsync(id, track: true, ct);
        if (word is null) return NotFound();

        await ApplyAsync(word, request, settings.KnownLanguages, ct);
        if (word.TermNormalized.Length == 0)
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "The term is empty after sanitization.");
        await db.SaveChangesAsync(ct);

        var updated = await LoadOwnedWordAsync(id, track: false, ct);
        return ToDto(updated!);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var word = await db.Words
            .FirstOrDefaultAsync(w => w.Id == id && w.UserId == currentUser.UserId, ct);
        if (word is null) return NotFound();

        db.Words.Remove(word); // translations, tag links, and SRS states cascade
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Words visible right now: the user's words in their current learning language.</summary>
    private async Task<IQueryable<Word>> ScopedWordsAsync(CancellationToken ct)
    {
        var settings = await settingsService.GetOrCreateAsync(currentUser.UserId, ct);
        return db.Words.Where(w =>
            w.UserId == currentUser.UserId && w.LanguageCode == settings.LearningLanguage);
    }

    private async Task<Word?> LoadOwnedWordAsync(Guid id, bool track, CancellationToken ct)
    {
        var query = db.Words
            .Include(w => w.Translations)
            .Include(w => w.WordTags).ThenInclude(wt => wt.Tag)
            .Where(w => w.Id == id && w.UserId == currentUser.UserId);
        if (!track) query = query.AsNoTracking();
        return await query.FirstOrDefaultAsync(ct);
    }

    private ActionResult? ValidateTranslations(SaveWordRequest request, List<string> knownLanguages)
    {
        var codes = (request.Translations ?? []).Select(t => t.LanguageCode).ToList();
        if (codes.Distinct().Count() != codes.Count)
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Duplicate translation language.");
        var unknown = codes.FirstOrDefault(c => !knownLanguages.Contains(c));
        return unknown is null
            ? null
            : Problem(statusCode: StatusCodes.Status400BadRequest,
                title: $"'{unknown}' is not one of your known languages.");
    }

    /// <summary>Applies a save request: sanitizes HTML, upserts translations, resolves tags.</summary>
    private async Task ApplyAsync(Word word, SaveWordRequest request, List<string> knownLanguages, CancellationToken ct)
    {
        word.Term = sanitizer.Sanitize(request.Term);
        word.TermNormalized = TermNormalizer.Normalize(word.Term);
        word.Transcription = string.IsNullOrWhiteSpace(request.Transcription) ? null : request.Transcription.Trim();
        word.PartOfSpeech = string.IsNullOrWhiteSpace(request.PartOfSpeech) ? null : request.PartOfSpeech.Trim();
        word.Gender = string.IsNullOrWhiteSpace(request.Gender) ? null : request.Gender.Trim();
        word.Example = sanitizer.SanitizeOrNull(request.Example);
        word.Notes = sanitizer.SanitizeOrNull(request.Notes);
        word.UpdatedAt = DateTime.UtcNow;

        // Translations: upsert those sent; remove currently-known languages that were omitted.
        // Translations in languages the user no longer knows are preserved untouched (spec FR-S6).
        var requested = request.Translations ?? [];
        var requestedCodes = requested.Select(t => t.LanguageCode).ToHashSet();
        word.Translations.RemoveAll(t =>
            knownLanguages.Contains(t.LanguageCode) && !requestedCodes.Contains(t.LanguageCode));
        foreach (var incoming in requested)
        {
            var existing = word.Translations.FirstOrDefault(t => t.LanguageCode == incoming.LanguageCode);
            if (existing is null)
            {
                existing = new WordTranslation { LanguageCode = incoming.LanguageCode };
                word.Translations.Add(existing);
            }
            existing.Text = sanitizer.Sanitize(incoming.Text);
            existing.ExampleTranslation = sanitizer.SanitizeOrNull(incoming.ExampleTranslation);
        }

        // Tags: replace the set; create missing tags for this user on the fly.
        var tagNames = (request.Tags ?? [])
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        // Load all of the user's tags (small set) so "Animals" vs "animals" reuses
        // the existing tag instead of creating a case-variant duplicate.
        var existingTags = await db.Tags
            .Where(t => t.UserId == currentUser.UserId)
            .ToListAsync(ct);
        var resolved = new List<Tag>();
        foreach (var name in tagNames)
        {
            var tagEntity = existingTags.FirstOrDefault(t =>
                string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
            if (tagEntity is null)
            {
                tagEntity = new Tag { UserId = currentUser.UserId, Name = name };
                db.Tags.Add(tagEntity);
            }
            resolved.Add(tagEntity);
        }
        // Same DbContext → EF identity resolution makes reference comparison safe here.
        word.WordTags.RemoveAll(wt => !resolved.Contains(wt.Tag));
        foreach (var tagEntity in resolved)
        {
            if (!word.WordTags.Any(wt => wt.Tag == tagEntity))
                word.WordTags.Add(new WordTag { Tag = tagEntity });
        }
    }

    private static WordDto ToDto(Word w) => WordMapper.ToDto(w);
}
