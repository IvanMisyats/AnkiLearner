using System.Globalization;
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
    SettingsService settingsService,
    FsrsInitializer fsrsInitializer) : ControllerBase
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
            var reviewedToday = await ReviewedTodayAsync(scope.Words, exercise, now, ct);
            counts.Add(new StudyCountsDto(exercise, due, Math.Min(newCount, allowance), reviewedToday));
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
        // JSON binding accepts any number for an enum.
        if (!Enum.IsDefined(request.Grade) || !Enum.IsDefined(request.Exercise))
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Unknown grade or exercise.");

        var settings = await settingsService.GetOrCreateAsync(currentUser.UserId, ct);
        var p = settings.SchedulerParameters();
        var now = DateTime.UtcNow;

        // Grading itself is never restricted by the tag filter — only the next card is.
        var word = await db.Words.FirstOrDefaultAsync(w =>
            w.Id == request.WordId &&
            w.UserId == currentUser.UserId &&
            w.LanguageCode == settings.LearningLanguage, ct);
        if (word is null) return NotFound();

        var state = await db.SrsStates.FirstOrDefaultAsync(
            s => s.WordId == word.Id && s.Exercise == request.Exercise, ct);
        var card = state is null ? null : await fsrsInitializer.CurrentMemoryAsync(state, p, ct);
        var before = state is null ? null : SrsSnapshot.Of(state).WithMemory(card);
        // A lapse = forgetting a card that had a day interval (not a new card, not a repeat).
        var lapse = request.Grade == ReviewGrade.Again && card is not null && state!.IntervalDays > 0;
        if (state is null)
        {
            state = new SrsState { WordId = word.Id, Exercise = request.Exercise, CreatedAt = now };
            db.SrsStates.Add(state);
        }

        // The same fuzz seed as the button hint (BuildCard), so the hint is what gets scheduled.
        var review = Fsrs.Review(card, request.Grade, now, p,
            Fsrs.FuzzUnit(word.Id, request.Exercise, card?.LastReviewed));
        state.Stability = review.Memory.Stability;
        state.Difficulty = review.Memory.Difficulty;
        state.IntervalDays = review.IntervalDays;
        state.Due = review.Due;
        state.LastReviewed = now;
        if (lapse) state.Lapses += 1;

        db.ReviewLog.Add(ReviewLogEntry.Create(
            word.Id, request.Exercise, ReviewKind.Review, request.Grade, now, before, state, review.Retrievability));
        try
        {
            await db.SaveChangesAsync(ct); // state + log row in one transaction
        }
        catch (Exception e) when (e is DbUpdateConcurrencyException || DbErrors.IsUniqueViolation(e))
        {
            // Graded on another device (or set up) since it was read, or two first grades of a
            // new card raced; nothing was saved.
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "This card was just updated elsewhere. Try again.");
        }

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

    // --- selection logic (spec FR-R4/FR-R9): due "Again" repeats, then new words (newest first,
    // within a daily limit), then due reviews (highest recall first), then learn-ahead. With no
    // daily limit, new words come after the due reviews instead. ---

    private async Task<StudyNextResponse> BuildNextAsync(
        StudyScope scope, ExerciseType exercise, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var p = scope.Settings.SchedulerParameters();
        var due = await DueStates(scope.Words, exercise, now)
            .Select(s => new DueCard(s.WordId, s.Stability, s.LastReviewed, s.IntervalDays, s.Due))
            .ToListAsync(ct);
        var allowance = await NewAllowanceAsync(scope.AllWords, exercise, scope.Settings.DailyNewLimit, now, ct);
        var limited = scope.Settings.DailyNewLimit > 0;

        // An "Again" repeat (or a Study again) is time-sensitive: it comes first.
        var wordId = due.Where(s => s.IntervalDays == 0)
            .OrderBy(s => s.Due).Select(s => (Guid?)s.WordId).FirstOrDefault();
        var isNew = false;

        // New words before the backlog, so a word added today (e.g. from Translate) is studied
        // today. Only with a daily limit: unlimited new words would starve every review.
        if (wordId is null && limited && allowance > 0)
        {
            wordId = await NewestNewWordAsync(scope.Words, exercise, ct);
            isNew = wordId is not null;
        }

        wordId ??= HighestRecall(due, now, p);

        if (wordId is null && !limited)
        {
            wordId = await NewestNewWordAsync(scope.Words, exercise, ct);
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
            .FirstOrDefaultAsync(s => s.WordId == word.Id && s.Exercise == exercise, ct);
        var card = state is null ? null : await fsrsInitializer.CurrentMemoryAsync(state, p, ct);
        // No memory = FSRS treats the first grade as a new card's (also a never-studied direction
        // that "Study again" made due).
        isNew = card is null;

        return new StudyNextResponse(BuildCard(word, exercise, scope.Settings, card, isNew, now), remaining);
    }

    private sealed record DueCard(Guid WordId, double? Stability, DateTime? LastReviewed, int IntervalDays, DateTime Due);

    /// <summary>Newest first; Id breaks ties: an import stamps all its words with one CreatedAt.</summary>
    private static Task<Guid?> NewestNewWordAsync(IQueryable<Word> words, ExerciseType exercise, CancellationToken ct) =>
        NewWords(words, exercise)
            .OrderByDescending(w => w.CreatedAt)
            .ThenByDescending(w => w.Id)
            .Select(w => (Guid?)w.Id)
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// The due card most likely still remembered. With a backlog, a card that just crossed the
    /// threshold is cheap to keep and losing recall fastest; a long-forgotten one loses little more
    /// by waiting. Cards without FSRS memory yet follow, oldest due first. Computed in memory: the
    /// due set is small.
    /// </summary>
    private static Guid? HighestRecall(List<DueCard> due, DateTime now, FsrsParameters p) =>
        due
            .OrderByDescending(s => s is { Stability: { } st, LastReviewed: { } last }
                ? Fsrs.Retrievability(new MemoryState(st, 5), Fsrs.ElapsedDays(last, now), p.Weights) // R ignores D
                : -1)
            .ThenBy(s => s.Due)
            .ThenBy(s => s.WordId)
            .Select(s => (Guid?)s.WordId)
            .FirstOrDefault();

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

    /// <summary>Distinct words reviewed today (UTC day, per exercise). Only the latest review is
    /// kept per state, so several presses on the same word count once, and resetting or deleting a
    /// word drops it out of today's number.</summary>
    private async Task<int> ReviewedTodayAsync(
        IQueryable<Word> words, ExerciseType exercise, DateTime now, CancellationToken ct)
    {
        var todayUtc = now.Date;
        return await db.SrsStates.CountAsync(s =>
            s.Exercise == exercise && s.LastReviewed >= todayUtc && words.Any(w => w.Id == s.WordId), ct);
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
        Word word, ExerciseType exercise, UserSettings settings, FsrsCard? card, bool isNew, DateTime now)
    {
        var translationsHtml = TranslationsHtml(word, settings.KnownLanguages);
        var (prompt, answer) = exercise == ExerciseType.TargetToKnown
            ? (word.Term, translationsHtml)
            : (translationsHtml, word.Term);

        // Each hint is exactly what that grade would schedule (same fuzz seed as Grade).
        var p = settings.SchedulerParameters();
        var unit = Fsrs.FuzzUnit(word.Id, exercise, card?.LastReviewed);
        string Hint(ReviewGrade grade) => FormatDays(Fsrs.Review(card, grade, now, p, unit).IntervalDays);
        var intervals = new StudyIntervalsDto(
            $"{Fsrs.AgainMinutes} min", Hint(ReviewGrade.Hard), Hint(ReviewGrade.Good), Hint(ReviewGrade.Easy));

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

    /// <summary>"12 d", "3.5 mo", "1.3 y" — FSRS intervals get long.</summary>
    internal static string FormatDays(int days) => days switch
    {
        < 31 => $"{days} d",
        < 365 => string.Create(CultureInfo.InvariantCulture, $"{days / 30.44:0.#} mo"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{days / 365.25:0.#} y"),
    };
}
