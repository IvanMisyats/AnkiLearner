using AnkiLearner.Api.Contracts;
using AnkiLearner.Api.Services;
using AnkiLearner.Core.Abstractions;
using AnkiLearner.Core.Entities;
using AnkiLearner.Core.Srs;
using AnkiLearner.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AnkiLearner.Api.Controllers;

[ApiController]
[Route("api/study")]
[Authorize]
public class StudyController(
    AppDbContext db,
    ICurrentUser currentUser,
    SettingsService settingsService) : ControllerBase
{
    /// <summary>Serve "Again" cards up to this many minutes early when nothing else is left (FR-R9).</summary>
    private const int LearnAheadMinutes = 20;

    [HttpGet("counts")]
    public async Task<ActionResult<List<StudyCountsDto>>> Counts([FromQuery] string? tag, CancellationToken ct)
    {
        var scope = await ScopeAsync(tag, ct);
        var now = DateTime.UtcNow;

        var counts = new List<StudyCountsDto>();
        foreach (var exercise in new[] { ExerciseType.TargetToKnown, ExerciseType.KnownToTarget })
        {
            var due = await DueStates(scope.Words, exercise, now).CountAsync(ct);
            var newCount = await NewWords(scope.Words, exercise).CountAsync(ct);
            // The daily-new allowance is global per exercise, not per tag filter.
            var allowance = await NewAllowanceAsync(scope.AllWords, exercise, scope.Settings.DailyNewLimit, now, ct);
            counts.Add(new StudyCountsDto(exercise, due, Math.Min(newCount, allowance)));
        }
        return counts;
    }

    [HttpGet("next")]
    public async Task<ActionResult<StudyNextResponse>> Next(
        [FromQuery] ExerciseType exercise, [FromQuery] string? tag, CancellationToken ct)
    {
        var scope = await ScopeAsync(tag, ct);
        return await BuildNextAsync(scope, exercise, ct);
    }

    [HttpPost("grade")]
    public async Task<ActionResult<StudyNextResponse>> Grade(
        GradeRequest request, [FromQuery] string? tag, CancellationToken ct)
    {
        var settings = await settingsService.GetOrCreateAsync(currentUser.UserId, ct);
        var now = DateTime.UtcNow;

        // Grading itself is never restricted by the tag filter — only the next card is.
        var word = await db.Words.FirstOrDefaultAsync(w =>
            w.Id == request.WordId &&
            w.UserId == currentUser.UserId &&
            w.LanguageCode == settings.LearningLanguage, ct);
        if (word is null) return NotFound();

        var state = await db.SrsStates.FirstOrDefaultAsync(
            s => s.WordId == word.Id && s.Exercise == request.Exercise, ct);
        if (state is null)
        {
            state = new SrsState { WordId = word.Id, Exercise = request.Exercise, CreatedAt = now };
            db.SrsStates.Add(state);
        }

        Sm2.Apply(state, request.Grade, now);
        await db.SaveChangesAsync(ct);

        var nextScope = await ScopeAsync(tag, ct);
        return await BuildNextAsync(nextScope, request.Exercise, ct);
    }

    /// <summary>Study scope: <see cref="AllWords"/> is the user's full dictionary in the current
    /// learning language (used for the global daily-new allowance); <see cref="Words"/> is
    /// additionally narrowed by the optional comma-separated tag filter (OR semantics).</summary>
    private sealed record StudyScope(UserSettings Settings, IQueryable<Word> AllWords, IQueryable<Word> Words);

    private async Task<StudyScope> ScopeAsync(string? tag, CancellationToken ct)
    {
        var settings = await settingsService.GetOrCreateAsync(currentUser.UserId, ct);
        var allWords = db.Words.Where(w =>
            w.UserId == currentUser.UserId && w.LanguageCode == settings.LearningLanguage);
        var words = allWords;
        if (!string.IsNullOrWhiteSpace(tag))
        {
            var names = tag.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            words = words.Where(w => w.WordTags.Any(wt => names.Contains(wt.Tag.Name)));
        }
        return new StudyScope(settings, allWords, words);
    }

    // --- selection logic (spec FR-R4/FR-R9: due first, then new, then learn-ahead) ---

    private async Task<StudyNextResponse> BuildNextAsync(
        StudyScope scope, ExerciseType exercise, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        var wordId = await DueStates(scope.Words, exercise, now)
            .OrderBy(s => s.Due)
            .Select(s => (Guid?)s.WordId)
            .FirstOrDefaultAsync(ct);
        var isNew = false;

        // The daily-new allowance is global per exercise, not per tag filter.
        var allowance = await NewAllowanceAsync(scope.AllWords, exercise, scope.Settings.DailyNewLimit, now, ct);
        if (wordId is null && allowance > 0)
        {
            wordId = await NewWords(scope.Words, exercise)
                .OrderBy(w => w.CreatedAt)
                .Select(w => (Guid?)w.Id)
                .FirstOrDefaultAsync(ct);
            isNew = wordId is not null;
        }

        wordId ??= await LearnAheadStates(scope.Words, exercise, now)
            .OrderBy(s => s.Due)
            .Select(s => (Guid?)s.WordId)
            .FirstOrDefaultAsync(ct);

        var remaining = await RemainingAsync(scope.Words, exercise, allowance, now, ct);
        if (wordId is null)
            return new StudyNextResponse(null, remaining);

        var word = await db.Words
            .Include(w => w.Translations)
            .Include(w => w.WordTags).ThenInclude(wt => wt.Tag)
            .AsNoTracking()
            .FirstAsync(w => w.Id == wordId, ct);

        var state = await db.SrsStates.AsNoTracking()
            .FirstOrDefaultAsync(s => s.WordId == word.Id && s.Exercise == exercise, ct)
            ?? new SrsState();

        return new StudyNextResponse(BuildCard(word, exercise, scope.Settings, state, isNew), remaining);
    }

    private IQueryable<SrsState> DueStates(IQueryable<Word> words, ExerciseType exercise, DateTime now) =>
        db.SrsStates.Where(s => s.Exercise == exercise && s.Due <= now && words.Any(w => w.Id == s.WordId));

    private IQueryable<SrsState> LearnAheadStates(IQueryable<Word> words, ExerciseType exercise, DateTime now) =>
        db.SrsStates.Where(s => s.Exercise == exercise
            && s.Due > now && s.Due <= now.AddMinutes(LearnAheadMinutes)
            && words.Any(w => w.Id == s.WordId));

    private static IQueryable<Word> NewWords(IQueryable<Word> words, ExerciseType exercise) =>
        words.Where(w => !w.SrsStates.Any(s => s.Exercise == exercise));

    /// <summary>How many new cards may still be introduced today (UTC day, per exercise).</summary>
    private async Task<int> NewAllowanceAsync(
        IQueryable<Word> words, ExerciseType exercise, int dailyLimit, DateTime now, CancellationToken ct)
    {
        if (dailyLimit <= 0) return int.MaxValue; // 0 = unlimited
        var todayUtc = now.Date;
        var introducedToday = await db.SrsStates.CountAsync(s =>
            s.Exercise == exercise && s.CreatedAt >= todayUtc && words.Any(w => w.Id == s.WordId), ct);
        return Math.Max(0, dailyLimit - introducedToday);
    }

    private async Task<int> RemainingAsync(
        IQueryable<Word> words, ExerciseType exercise, int allowance, DateTime now, CancellationToken ct)
    {
        var due = await DueStates(words, exercise, now).CountAsync(ct);
        var learnAhead = await LearnAheadStates(words, exercise, now).CountAsync(ct);
        var fresh = await NewWords(words, exercise).CountAsync(ct);
        return due + learnAhead + Math.Min(fresh, allowance);
    }

    // --- card projection (spec FR-R2: combined known-language sides) ---

    private static StudyCardDto BuildCard(
        Word word, ExerciseType exercise, UserSettings settings, SrsState state, bool isNew)
    {
        var translationsHtml = TranslationsHtml(word, settings.KnownLanguages);
        var (prompt, answer) = exercise == ExerciseType.TargetToKnown
            ? (word.Term, translationsHtml)
            : (translationsHtml, word.Term);

        var intervals = new StudyIntervalsDto(
            FormatInterval(Sm2.PreviewDays(state, ReviewGrade.Again), ReviewGrade.Again),
            FormatInterval(Sm2.PreviewDays(state, ReviewGrade.Hard), ReviewGrade.Hard),
            FormatInterval(Sm2.PreviewDays(state, ReviewGrade.Good), ReviewGrade.Good),
            FormatInterval(Sm2.PreviewDays(state, ReviewGrade.Easy), ReviewGrade.Easy));

        return new StudyCardDto(WordMapper.ToDto(word), prompt, answer, isNew, intervals);
    }

    /// <summary>All known-language translations combined, in the user's language order.</summary>
    private static string TranslationsHtml(Word word, List<string> knownLanguages)
    {
        var visible = knownLanguages
            .Select(code => word.Translations.FirstOrDefault(t => t.LanguageCode == code))
            .Where(t => t is not null)
            .Cast<WordTranslation>()
            .ToList();
        if (visible.Count == 0) return string.Empty;
        if (visible.Count == 1) return visible[0].Text;
        return string.Join("", visible.Select(t =>
            $"""<div class="translation"><span class="lang">{t.LanguageCode.ToUpperInvariant()}</span> {t.Text}</div>"""));
    }

    private static string FormatInterval(int days, ReviewGrade grade) =>
        grade == ReviewGrade.Again ? $"{Sm2.AgainMinutes} min" : $"{days} d";
}
