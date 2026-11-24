using System.Net;
using System.Net.Http.Json;
using AnkiLearner.Api.Contracts;
using AnkiLearner.Api.Services;
using AnkiLearner.Core.Entities;
using AnkiLearner.Core.Srs;
using AnkiLearner.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AnkiLearner.Tests.Integration;

/// <summary>FSRS in the study flow: setting up pre-FSRS states, re-setup, study order (spec §5, FR-R4).</summary>
public class FsrsFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateTime AnkiEra = new(2025, 6, 1, 18, 0, 0, DateTimeKind.Utc);

    // --- setup ---

    [Fact]
    public async Task SetUp_GivesLegacyStatesMemory_LogsIt_AndCanBeRedoneFromTheSameStart()
    {
        var (client, userId) = await NewUserAsync();
        var word = await CreateWordAsync(client, "hund", "dog");
        var due = new DateTime(2025, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        await SeedLegacyStateAsync(word.Id, intervalDays: 30, lapses: 0, repetitions: 4, lastReviewed: null, due);

        Assert.Equal(1, await SetUpAsync(userId, onlyUnset: true));
        var first = await StateAsync(word.Id);
        Assert.Equal(30, first.Stability);
        Assert.Equal(due.AddDays(-30), first.LastReviewed);
        Assert.Equal(first.LastReviewed!.Value.AddDays(first.IntervalDays), first.Due);

        var setup = Assert.Single(await LogAsync(word.Id));
        Assert.Equal(ReviewKind.Setup, setup.Kind);
        Assert.Equal(30, setup.IntervalBefore);
        Assert.Equal(due, setup.DueBefore);

        // Nothing left without memory; a full re-run starts from the logged SM-2 values again,
        // arrives at the same result, and so changes nothing and logs nothing.
        Assert.Equal(0, await SetUpAsync(userId, onlyUnset: true));
        Assert.Equal(0, await SetUpAsync(userId, onlyUnset: false));
        var again = await StateAsync(word.Id);
        Assert.Equal(first.Stability, again.Stability);
        Assert.Equal(first.Difficulty, again.Difficulty);
        Assert.Equal(first.Due, again.Due);
        Assert.Single(await LogAsync(word.Id));
    }

    [Fact]
    public async Task SrsState_RowVersion_RejectsAStaleSave()
    {
        // The guard that stops a setup run from overwriting a card graded meanwhile.
        var (client, _) = await NewUserAsync();
        var word = await CreateWordAsync(client, "tog", "train");
        await GradeAsync(client, word.Id, "Good");

        using var staleScope = factory.Services.CreateScope();
        var staleDb = staleScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stale = await staleDb.SrsStates.SingleAsync(s => s.WordId == word.Id);

        await GradeAsync(client, word.Id, "Good"); // someone else updates the row

        stale.IntervalDays = 999;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => staleDb.SaveChangesAsync());
    }

    [Fact]
    public async Task StudyAgain_TwiceOnANeverStudiedDirection_LeavesItWithoutMemory()
    {
        var (client, userId) = await NewUserAsync();
        var word = await CreateWordAsync(client, "skov", "forest");
        (await client.PostAsync($"/api/words/{word.Id}/relearn", null)).EnsureSuccessStatusCode();
        (await client.PostAsync($"/api/words/{word.Id}/relearn", null)).EnsureSuccessStatusCode();

        Assert.Equal(0, await SetUpAsync(userId, onlyUnset: false));
        var state = await StateAsync(word.Id);
        Assert.Null(state.Stability);
        Assert.Equal(0, state.Lapses);
        Assert.True(state.Due <= DateTime.UtcNow); // still due now
    }

    [Fact]
    public async Task SetUp_ReplaysAnkiHistory_ForACardNotReviewedOnTheSite()
    {
        var (client, userId) = await NewUserAsync();
        var word = await CreateWordAsync(client, "kat", "cat");
        var history = new[]
        {
            (AnkiEra, ReviewGrade.Good), (AnkiEra.AddDays(3), ReviewGrade.Good),
            (AnkiEra.AddDays(12), ReviewGrade.Easy),
        };
        // An imported state is dated back to the Anki collection's creation, which can be later than
        // the first reviews (decks moved into a newer collection): the whole history still counts.
        await SeedLegacyStateAsync(word.Id, intervalDays: 40, lapses: 0, repetitions: 3, lastReviewed: null,
            due: AnkiEra.AddDays(52), createdAt: AnkiEra.AddDays(5));
        await SeedAnkiHistoryAsync(word.Id, history);

        await SetUpAsync(userId, onlyUnset: true);

        var expected = Fsrs.Replay(history, Fsrs.DefaultWeights)!.Value;
        var state = await StateAsync(word.Id);
        Assert.Equal(expected.Memory.Stability, state.Stability!.Value, 1e-9);
        Assert.Equal(expected.Memory.Difficulty, state.Difficulty!.Value, 1e-9);
        Assert.Equal(expected.LastReviewed, state.LastReviewed!.Value, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task SetUp_IgnoresAnkiHistoryOlderThanTheState()
    {
        // The word was reset (or re-added) before the log existed: its state was created on the
        // site, after the word, and its old Anki history is stale.
        var (client, userId) = await NewUserAsync();
        var word = await CreateWordAsync(client, "fugl", "bird");
        var now = DateTime.UtcNow;
        await SeedLegacyStateAsync(word.Id, intervalDays: 6, lapses: 0, repetitions: 2,
            lastReviewed: now, due: now.AddDays(6), createdAt: now);
        await SeedAnkiHistoryAsync(word.Id, [(AnkiEra, ReviewGrade.Again), (AnkiEra.AddDays(1), ReviewGrade.Again)]);

        await SetUpAsync(userId, onlyUnset: true);

        var state = await StateAsync(word.Id);
        Assert.Equal(FsrsSetup.HeuristicDifficulty(0, 2, Fsrs.DefaultWeights), state.Difficulty); // not the history's
    }

    [Fact]
    public async Task NewWeights_RedoTheSetupOfUnstudiedCards_ButNotOfStudiedOnes()
    {
        var (client, userId) = await NewUserAsync();
        var studied = await CreateWordAsync(client, "sol", "sun");
        var unstudied = await CreateWordAsync(client, "måne", "moon");
        var due = new DateTime(2025, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        await SeedLegacyStateAsync(studied.Id, 20, 0, 4, null, due);
        await SeedLegacyStateAsync(unstudied.Id, 20, 0, 4, null, due);
        await SetUpAsync(userId, onlyUnset: true);
        await GradeAsync(client, studied.Id, "Good");
        var studiedBefore = await StateAsync(studied.Id);
        var unstudiedBefore = await StateAsync(unstudied.Id);

        // Retention 0.80 instead of 0.90: the unstudied card's interval gets longer.
        (await client.PutAsJsonAsync("/api/settings", new
        {
            learningLanguage = "da", knownLanguages = new[] { "en" }, dailyNewLimit = 20, desiredRetention = 0.80,
        })).EnsureSuccessStatusCode();

        var unstudiedAfter = await StateAsync(unstudied.Id);
        Assert.True(unstudiedAfter.IntervalDays > unstudiedBefore.IntervalDays);
        Assert.Equal(unstudiedBefore.Stability, unstudiedAfter.Stability);
        var studiedAfter = await StateAsync(studied.Id);
        Assert.Equal(studiedBefore.Due, studiedAfter.Due);
        Assert.Equal(studiedBefore.Stability, studiedAfter.Stability);
    }

    [Fact]
    public async Task SetUp_AfterAReset_DoesNotReuseTheOldStatesSetup()
    {
        var (client, userId) = await NewUserAsync();
        var word = await CreateWordAsync(client, "regn", "rain");
        await SeedLegacyStateAsync(word.Id, 50, 0, 6, null, new DateTime(2025, 9, 1, 0, 0, 0, DateTimeKind.Utc));
        await SetUpAsync(userId, onlyUnset: true);

        (await client.DeleteAsync($"/api/words/{word.Id}/progress")).EnsureSuccessStatusCode();
        (await client.PostAsync($"/api/words/{word.Id}/relearn", null)).EnsureSuccessStatusCode();

        // The relearned state is brand new (never reviewed): nothing to set up, and the old
        // Setup row (interval 50) must not be read as its starting point.
        Assert.Equal(0, await SetUpAsync(userId, onlyUnset: false));
        var state = await StateAsync(word.Id);
        Assert.Null(state.Stability);
    }

    [Fact]
    public async Task StartupService_SetsUpEveryUsersLegacyStates()
    {
        var (client, _) = await NewUserAsync();
        var word = await CreateWordAsync(client, "sne", "snow");
        await SeedLegacyStateAsync(word.Id, 12, 1, 0, new DateTime(2026, 11, 10, 8, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 11, 22, 8, 0, 0, DateTimeKind.Utc));

        var service = new FsrsSetupOnStartup(
            factory.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<FsrsSetupOnStartup>.Instance);
        await service.StartAsync(CancellationToken.None);
        await service.ExecuteTask!;

        var state = await StateAsync(word.Id);
        Assert.Equal(12, state.Stability);
        Assert.Equal(FsrsSetup.HeuristicDifficulty(1, 0, Fsrs.DefaultWeights), state.Difficulty);
    }

    // --- grading ---

    [Fact]
    public async Task Grading_ALegacyStateNotSetUpYet_SetsItUpOnTheSpot()
    {
        var (client, _) = await NewUserAsync();
        var word = await CreateWordAsync(client, "vind", "wind");
        var lastReviewed = DateTime.UtcNow.Date.AddDays(-20).AddHours(9);
        await SeedLegacyStateAsync(word.Id, 20, 0, 3, lastReviewed, lastReviewed.AddDays(20));

        await GradeAsync(client, word.Id, "Good");

        var review = Assert.Single(await LogAsync(word.Id));
        var expected = Fsrs.Next(new MemoryState(20, FsrsSetup.HeuristicDifficulty(0, 3, Fsrs.DefaultWeights)),
            20, ReviewGrade.Good, Fsrs.DefaultWeights);
        Assert.Equal(expected.Stability, review.StabilityAfter!.Value, 1e-9);
        Assert.NotNull(review.Retrievability);
    }

    [Fact]
    public async Task Grading_RejectsAnUndefinedGrade()
    {
        var (client, _) = await NewUserAsync();
        var word = await CreateWordAsync(client, "sky", "cloud");

        var response = await client.PostAsJsonAsync("/api/study/grade",
            new { wordId = word.Id, exercise = "TargetToKnown", grade = 7 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // --- study order ---

    [Fact]
    public async Task AnAgainRepeat_ComesBeforeNewWords()
    {
        var (client, _) = await NewUserAsync();
        var failed = await CreateWordAsync(client, "svær", "hard");
        var now = DateTime.UtcNow;
        // Pressed Again 15 minutes ago: its 10-minute repeat is due.
        await SeedMemoryStateAsync(failed.Id, stability: 0.5, now.AddMinutes(-15), due: now.AddMinutes(-5), intervalDays: 0);
        await CreateWordAsync(client, "ny", "new");

        var next = await (await client.GetAsync("/api/study/next?exercise=TargetToKnown")).ReadAsAsync<NextDto>();

        Assert.Equal("svær", next.Card!.Prompt);
        Assert.False(next.Card.IsNew);
    }

    [Fact]
    public async Task WithNoDailyLimit_DueReviewsComeBeforeNewWords()
    {
        var (client, _) = await NewUserAsync();
        (await client.PutAsJsonAsync("/api/settings",
            new { learningLanguage = "da", knownLanguages = new[] { "en" }, dailyNewLimit = 0 })).EnsureSuccessStatusCode();
        var old = await CreateWordAsync(client, "gammel", "old");
        await SeedMemoryStateAsync(old.Id, stability: 10, DateTime.UtcNow.AddDays(-40));
        await CreateWordAsync(client, "ny", "new");

        var next = await (await client.GetAsync("/api/study/next?exercise=TargetToKnown")).ReadAsAsync<NextDto>();

        Assert.Equal("gammel", next.Card!.Prompt);
    }

    [Fact]
    public async Task NewWords_ComeBeforeDueCards_WithinTheDailyLimit()
    {
        var (client, _) = await NewUserAsync();
        var old = await CreateWordAsync(client, "gammel", "old");
        var lastReviewed = DateTime.UtcNow.AddDays(-40);
        await SeedMemoryStateAsync(old.Id, stability: 10, lastReviewed); // long overdue
        await CreateWordAsync(client, "ny", "new");

        var next = await (await client.GetAsync("/api/study/next?exercise=TargetToKnown")).ReadAsAsync<NextDto>();
        Assert.True(next.Card!.IsNew);
        Assert.Equal("ny", next.Card.Prompt);

        // With the daily limit used up, the due card comes next.
        (await client.PutAsJsonAsync("/api/settings",
            new { learningLanguage = "da", knownLanguages = new[] { "en" }, dailyNewLimit = 1 })).EnsureSuccessStatusCode();
        await GradeAsync(client, next.Card.Word.Id, "Good");
        var afterLimit = await (await client.GetAsync("/api/study/next?exercise=TargetToKnown")).ReadAsAsync<NextDto>();
        Assert.Equal("gammel", afterLimit.Card!.Prompt);
        Assert.False(afterLimit.Card.IsNew);
    }

    [Fact]
    public async Task DueCards_ComeHighestRecallFirst()
    {
        var (client, _) = await NewUserAsync();
        (await client.PutAsJsonAsync("/api/settings",
            new { learningLanguage = "da", knownLanguages = new[] { "en" }, dailyNewLimit = 0 })).EnsureSuccessStatusCode();
        var now = DateTime.UtcNow;
        var forgotten = await CreateWordAsync(client, "glemt", "forgotten");
        var justDue = await CreateWordAsync(client, "frisk", "fresh");
        // Both due; "glemt" was due long ago (R ≈ 0.6), "frisk" only just (R ≈ 0.9).
        await SeedMemoryStateAsync(forgotten.Id, stability: 5, now.AddDays(-60), due: now.AddDays(-55));
        await SeedMemoryStateAsync(justDue.Id, stability: 30, now.AddDays(-31), due: now.AddDays(-1));

        var next = await (await client.GetAsync("/api/study/next?exercise=TargetToKnown")).ReadAsAsync<NextDto>();

        Assert.Equal("frisk", next.Card!.Prompt);
    }

    // --- helpers ---

    private async Task<(HttpClient Client, Guid UserId)> NewUserAsync()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var me = await (await client.GetAsync("/api/auth/me")).ReadAsAsync<MeResponse>();
        return (client, me.User.Id);
    }

    private async Task<int> SetUpAsync(Guid userId, bool onlyUnset)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<FsrsInitializer>()
            .SetUpAsync(userId, onlyUnset, CancellationToken.None);
    }

    private async Task SeedLegacyStateAsync(
        Guid wordId, int intervalDays, int lapses, int repetitions, DateTime? lastReviewed, DateTime due,
        DateTime? createdAt = null, ExerciseType exercise = ExerciseType.TargetToKnown)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.SrsStates.Add(new SrsState
        {
            WordId = wordId, Exercise = exercise, IntervalDays = intervalDays, Lapses = lapses,
            Repetitions = repetitions, LastReviewed = lastReviewed, Due = due, EaseFactor = 2.5,
            CreatedAt = createdAt ?? new DateTime(2025, 5, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedMemoryStateAsync(
        Guid wordId, double stability, DateTime lastReviewed, DateTime? due = null, int? intervalDays = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.SrsStates.Add(new SrsState
        {
            WordId = wordId, Exercise = ExerciseType.TargetToKnown, Stability = stability, Difficulty = 5,
            LastReviewed = lastReviewed, IntervalDays = intervalDays ?? Math.Max(1, (int)stability),
            Due = due ?? lastReviewed.AddDays(stability),
            CreatedAt = lastReviewed.AddDays(-30),
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedAnkiHistoryAsync(Guid wordId, (DateTime At, ReviewGrade Grade)[] history)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var (at, grade) in history)
            db.ReviewLog.Add(new ReviewLogEntry
            {
                WordId = wordId, Exercise = ExerciseType.TargetToKnown, Kind = ReviewKind.Review,
                Source = ReviewSource.Anki, Grade = grade, ReviewedAt = at,
                AnkiRevlogId = new DateTimeOffset(at).ToUnixTimeMilliseconds(),
            });
        await db.SaveChangesAsync();
    }

    private async Task<SrsState> StateAsync(Guid wordId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.SrsStates.AsNoTracking()
            .SingleAsync(s => s.WordId == wordId && s.Exercise == ExerciseType.TargetToKnown);
    }

    private async Task<List<ReviewLogEntry>> LogAsync(Guid wordId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.ReviewLog.AsNoTracking()
            .Where(r => r.WordId == wordId && r.Source == ReviewSource.Site)
            .OrderBy(r => r.Id).ToListAsync();
    }

    private static async Task GradeAsync(HttpClient client, Guid wordId, string grade) =>
        (await client.PostAsJsonAsync("/api/study/grade",
            new { wordId, exercise = "TargetToKnown", grade })).EnsureSuccessStatusCode();

    private static async Task<WordDto> CreateWordAsync(HttpClient client, string term, string enText)
    {
        var response = await client.PostAsJsonAsync("/api/words", new
        {
            term,
            translations = new[] { new { languageCode = "en", text = enText, exampleTranslation = (string?)null } },
            tags = Array.Empty<string>(),
        });
        return await response.ReadAsAsync<WordDto>();
    }

    private record CardDto(WordDto Word, string Prompt, bool IsNew);
    private record NextDto(CardDto? Card, int Remaining);
}
