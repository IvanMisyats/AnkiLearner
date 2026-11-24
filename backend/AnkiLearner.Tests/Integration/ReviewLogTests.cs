using System.Net;
using System.Net.Http.Json;
using AnkiLearner.Api.Contracts;
using AnkiLearner.Core.Entities;
using AnkiLearner.Core.Srs;
using AnkiLearner.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AnkiLearner.Tests.Integration;

/// <summary>Every grade, relearn and reset appends to the review log (spec FR-R11).</summary>
public class ReviewLogTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Grading_LogsEveryPress_WithStateBeforeAndAfter()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var word = await CreateWordAsync(client, "hund", "dog");

        await GradeAsync(client, word.Id, "TargetToKnown", "Again");
        await GradeAsync(client, word.Id, "TargetToKnown", "Good");

        var log = await LogAsync(word.Id);
        Assert.Equal(2, log.Count);
        Assert.All(log, r =>
        {
            Assert.Equal(ReviewKind.Review, r.Kind);
            Assert.Equal(ReviewSource.Site, r.Source);
            Assert.Equal(ExerciseType.TargetToKnown, r.Exercise);
        });

        // First press on a new card: no state existed before it.
        var again = log[0];
        Assert.Equal(ReviewGrade.Again, again.Grade);
        Assert.Null(again.PreviousReviewAt);
        Assert.Null(again.IntervalBefore);
        Assert.Null(again.EaseBefore);
        Assert.Null(again.DueBefore);
        Assert.Equal(0, again.IntervalAfter);
        Assert.Equal(2.5, again.EaseAfter); // legacy SM-2 ease, untouched by FSRS
        Assert.Null(again.StabilityBefore);
        Assert.Null(again.Retrievability);
        var initial = Fsrs.Initial(ReviewGrade.Again, Fsrs.DefaultWeights);
        Assert.Equal(initial.Stability, again.StabilityAfter);
        Assert.Equal(initial.Difficulty, again.DifficultyAfter);
        Assert.Equal(again.ReviewedAt.AddMinutes(Fsrs.AgainMinutes), again.DueAfter!.Value, TimeSpan.FromMilliseconds(1));

        // The repeat in the same session is logged too, chained to the first press.
        var good = log[1];
        Assert.Equal(ReviewGrade.Good, good.Grade);
        Assert.Equal(again.ReviewedAt, good.PreviousReviewAt!.Value, TimeSpan.FromMilliseconds(1));
        Assert.Equal(again.IntervalAfter, good.IntervalBefore);
        Assert.Equal(again.EaseAfter, good.EaseBefore);
        Assert.Equal(again.DueAfter!.Value, good.DueBefore!.Value, TimeSpan.FromMilliseconds(1));
        Assert.Equal(again.StabilityAfter, good.StabilityBefore);
        Assert.Equal(again.DifficultyAfter, good.DifficultyBefore);
        Assert.Equal(1.0, good.Retrievability); // same day
        Assert.Equal(2, good.IntervalAfter); // tiny S → Hard 1 d, so Good is kept at 2 d

        // The log mirrors the state the grade produced.
        var state = await StateAsync(word.Id, ExerciseType.TargetToKnown);
        Assert.Equal(state.IntervalDays, good.IntervalAfter);
        Assert.Equal(state.EaseFactor, good.EaseAfter);
        Assert.Equal(state.Stability, good.StabilityAfter);
        Assert.Equal(state.Difficulty, good.DifficultyAfter);
        Assert.Equal(state.Due, good.DueAfter!.Value, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task Relearn_LogsBothDirections_IncludingANeverStudiedOne()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var word = await CreateWordAsync(client, "glemme", "to forget");
        await GradeAsync(client, word.Id, "TargetToKnown", "Good"); // KnownToTarget stays unstudied

        var response = await client.PostAsync($"/api/words/{word.Id}/relearn", null);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var relearns = (await LogAsync(word.Id)).Where(r => r.Kind == ReviewKind.Relearn).ToList();
        Assert.Equal(2, relearns.Count);
        Assert.All(relearns, r => Assert.Null(r.Grade));

        var studied = relearns.Single(r => r.Exercise == ExerciseType.TargetToKnown);
        Assert.Equal(2, studied.IntervalBefore); // a new card's first Good: 2 d
        Assert.Equal(0, studied.IntervalAfter);
        Assert.NotNull(studied.PreviousReviewAt);
        // Forgotten: Stability drops, Difficulty is kept (no penalty).
        Assert.True(studied.StabilityAfter < studied.StabilityBefore);
        Assert.Equal(studied.DifficultyBefore, studied.DifficultyAfter);
        Assert.Equal(1.0, studied.Retrievability); // relearned the same day it was graded

        var unstudied = relearns.Single(r => r.Exercise == ExerciseType.KnownToTarget);
        Assert.Null(unstudied.IntervalBefore);
        Assert.Null(unstudied.StabilityAfter); // never reviewed: no memory to drop
        Assert.Null(unstudied.PreviousReviewAt);
        Assert.Equal(0, unstudied.IntervalAfter);
        Assert.NotNull(unstudied.DueAfter);
    }

    [Fact]
    public async Task ResetProgress_LogsAReset_AndKeepsTheHistory()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var word = await CreateWordAsync(client, "huske", "to remember");
        await GradeAsync(client, word.Id, "TargetToKnown", "Good");
        await GradeAsync(client, word.Id, "KnownToTarget", "Easy");

        var reset = await client.DeleteAsync($"/api/words/{word.Id}/progress");
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        var log = await LogAsync(word.Id);
        Assert.Equal(4, log.Count); // two reviews survive, plus one reset per direction
        var resets = log.Where(r => r.Kind == ReviewKind.Reset).ToList();
        Assert.Equal(2, resets.Count);
        Assert.All(resets, r =>
        {
            Assert.Null(r.Grade);
            Assert.NotNull(r.IntervalBefore);
            Assert.Null(r.IntervalAfter);
            Assert.Null(r.EaseAfter);
            Assert.Null(r.DueAfter);
        });

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.SrsStates.AnyAsync(s => s.WordId == word.Id));
    }

    [Fact]
    public async Task DeletingAWord_DeletesItsHistory()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var word = await CreateWordAsync(client, "sky", "cloud");
        await GradeAsync(client, word.Id, "TargetToKnown", "Hard");
        Assert.Single(await LogAsync(word.Id));

        var delete = await client.DeleteAsync($"/api/words/{word.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        Assert.Empty(await LogAsync(word.Id));
    }

    // --- helpers ---

    private async Task<List<ReviewLogEntry>> LogAsync(Guid wordId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.ReviewLog.AsNoTracking()
            .Where(r => r.WordId == wordId)
            .OrderBy(r => r.Id)
            .ToListAsync();
    }

    private async Task<SrsState> StateAsync(Guid wordId, ExerciseType exercise)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.SrsStates.AsNoTracking().SingleAsync(s => s.WordId == wordId && s.Exercise == exercise);
    }

    private static async Task GradeAsync(HttpClient client, Guid wordId, string exercise, string grade)
    {
        var response = await client.PostAsJsonAsync("/api/study/grade", new { wordId, exercise, grade });
        response.EnsureSuccessStatusCode();
    }

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
}
