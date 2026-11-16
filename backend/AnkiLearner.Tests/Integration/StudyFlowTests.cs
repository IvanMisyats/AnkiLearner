using System.Net;
using System.Net.Http.Json;
using AnkiLearner.Api.Contracts;

namespace AnkiLearner.Tests.Integration;

public class StudyFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task NewWords_AppearInCounts_AndAreServedAfterDueCards()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        await CreateWordAsync(client, "hund", "dog");
        await CreateWordAsync(client, "kat", "cat");

        var counts = await GetAsync<List<StudyCountsDto>>(client, "/api/study/counts");
        var t2k = Assert.Single(counts, c => c.Exercise == "TargetToKnown");
        Assert.Equal(0, t2k.Due);
        Assert.Equal(2, t2k.New);

        // First card served newest-first; it is new.
        var next = await GetAsync<StudyNextResponse>(client, "/api/study/next?exercise=TargetToKnown");
        Assert.NotNull(next.Card);
        Assert.True(next.Card.IsNew);
        Assert.Equal("kat", next.Card.Prompt);
        Assert.Contains("cat", next.Card.Answer);
        Assert.Equal(2, next.Remaining);
        Assert.Equal("10 min", next.Card.Intervals.Again);
        Assert.Equal("1 d", next.Card.Intervals.Good);
    }

    [Fact]
    public async Task Grading_AdvancesState_AndServesNextCard()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var first = await CreateWordAsync(client, "bord", "table");
        await CreateWordAsync(client, "stol", "chair");

        // Grade the first word Good → due tomorrow, second word becomes next.
        var afterGrade = await PostAsync<StudyNextResponse>(client, "/api/study/grade",
            new { wordId = first.Id, exercise = "TargetToKnown", grade = "Good" });
        Assert.NotNull(afterGrade.Card);
        Assert.Equal("stol", afterGrade.Card.Prompt);
        Assert.Equal(1, afterGrade.Remaining);

        // Direction is tracked independently: KnownToTarget still has 2 new cards.
        var counts = await GetAsync<List<StudyCountsDto>>(client, "/api/study/counts");
        Assert.Equal(2, counts.Single(c => c.Exercise == "KnownToTarget").New);
    }

    [Fact]
    public async Task KnownToTarget_SwapsPromptAndAnswer()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        await CreateWordAsync(client, "vindue", "window");

        var next = await GetAsync<StudyNextResponse>(client, "/api/study/next?exercise=KnownToTarget");
        Assert.NotNull(next.Card);
        Assert.Contains("window", next.Card.Prompt);
        Assert.Equal("vindue", next.Card.Answer);
    }

    [Fact]
    public async Task DailyNewLimit_CapsIntroductions_AndAgainCardsAreLearnAhead()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        await UpdateSettingsAsync(client, dailyNewLimit: 1);
        var word = await CreateWordAsync(client, "æble", "apple");
        await CreateWordAsync(client, "pære", "pear");

        // Only 1 new allowed today.
        var counts = await GetAsync<List<StudyCountsDto>>(client, "/api/study/counts");
        Assert.Equal(1, counts.Single(c => c.Exercise == "TargetToKnown").New);

        // Grade the first new card "Again" → rescheduled +10 min. The daily allowance
        // is used up, so the only thing left is the learn-ahead card itself.
        var next = await PostAsync<StudyNextResponse>(client, "/api/study/grade",
            new { wordId = word.Id, exercise = "TargetToKnown", grade = "Again" });
        Assert.NotNull(next.Card);
        Assert.Equal(word.Id, next.Card.Word.Id);
        Assert.False(next.Card.IsNew);
        Assert.Equal(1, next.Remaining);
    }

    [Fact]
    public async Task TagFilter_RestrictsStudyScope()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        await CreateWordAsync(client, "løbe", "to run", tags: ["verbs"]);
        await CreateWordAsync(client, "hus", "house", tags: ["nouns"]);

        var counts = await GetAsync<List<StudyCountsDto>>(client, "/api/study/counts?tag=verbs");
        Assert.Equal(1, counts.Single(c => c.Exercise == "TargetToKnown").New);

        var next = await GetAsync<StudyNextResponse>(client, "/api/study/next?exercise=TargetToKnown&tag=verbs");
        Assert.NotNull(next.Card);
        Assert.Equal("løbe", next.Card.Prompt);
        Assert.Equal(1, next.Remaining);
    }

    [Fact]
    public async Task DailyNewLimit_IsGlobal_NotPerTagFilter()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        await UpdateSettingsAsync(client, dailyNewLimit: 1);
        var wordA = await CreateWordAsync(client, "sol", "sun", tags: ["a"]);
        await CreateWordAsync(client, "måne", "moon", tags: ["b"]);

        // Use up the single daily slot inside tag "a"…
        await PostAsync<StudyNextResponse>(client, "/api/study/grade",
            new { wordId = wordA.Id, exercise = "TargetToKnown", grade = "Good" });

        // …then tag "b" must not grant a fresh allowance.
        var counts = await GetAsync<List<StudyCountsDto>>(client, "/api/study/counts?tag=b");
        Assert.Equal(0, counts.Single(c => c.Exercise == "TargetToKnown").New);
    }

    [Fact]
    public async Task Grading_AnotherUsersWord_Returns404()
    {
        var alice = await factory.CreateAuthenticatedClientAsync();
        var bob = await factory.CreateAuthenticatedClientAsync();
        var word = await CreateWordAsync(alice, "fugl", "bird");

        var response = await bob.PostAsJsonAsync("/api/study/grade",
            new { wordId = word.Id, exercise = "TargetToKnown", grade = "Good" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task BothTranslations_AppearOnCombinedKnownSide()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        await UpdateSettingsAsync(client, knownLanguages: ["en", "uk"]);
        await CreateWordAsync(client, "hund", "dog", ukText: "пес");

        var next = await GetAsync<StudyNextResponse>(client, "/api/study/next?exercise=TargetToKnown");
        Assert.NotNull(next.Card);
        Assert.Contains("dog", next.Card.Answer);
        Assert.Contains("пес", next.Card.Answer);
        Assert.Contains("EN", next.Card.Answer); // language labels when several languages
    }

    [Fact]
    public async Task ReviewedToday_CountsGradedWordsOncePerDirection()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var graded = await CreateWordAsync(client, "regn", "rain");
        await CreateWordAsync(client, "sne", "snow");

        var before = await GetAsync<List<StudyCountsDto>>(client, "/api/study/counts");
        Assert.Equal(0, before.Single(c => c.Exercise == "TargetToKnown").ReviewedToday);

        // Two presses on the same word count once: only the latest review is stored.
        await PostAsync<StudyNextResponse>(client, "/api/study/grade",
            new { wordId = graded.Id, exercise = "TargetToKnown", grade = "Again" });
        await PostAsync<StudyNextResponse>(client, "/api/study/grade",
            new { wordId = graded.Id, exercise = "TargetToKnown", grade = "Good" });

        var after = await GetAsync<List<StudyCountsDto>>(client, "/api/study/counts");
        Assert.Equal(1, after.Single(c => c.Exercise == "TargetToKnown").ReviewedToday);
        Assert.Equal(0, after.Single(c => c.Exercise == "KnownToTarget").ReviewedToday);
    }

    [Fact]
    public async Task ResetProgress_MakesTheWordNewAgain_InBothDirections()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var word = await CreateWordAsync(client, "sommerfugl", "butterfly");

        foreach (var exercise in new[] { "TargetToKnown", "KnownToTarget" })
            await PostAsync<StudyNextResponse>(client, "/api/study/grade",
                new { wordId = word.Id, exercise, grade = "Good" });

        var graded = await GetAsync<List<StudyCountsDto>>(client, "/api/study/counts");
        Assert.All(graded, c => Assert.Equal(0, c.New));
        Assert.All(graded, c => Assert.Equal(1, c.ReviewedToday));

        var reset = await client.DeleteAsync($"/api/words/{word.Id}/progress");
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        var after = await GetAsync<List<StudyCountsDto>>(client, "/api/study/counts");
        Assert.All(after, c => Assert.Equal(1, c.New));
        Assert.All(after, c => Assert.Equal(0, c.ReviewedToday));

        // Only the progress is forgotten — the word itself is untouched.
        var fetched = await (await client.GetAsync($"/api/words/{word.Id}")).ReadAsAsync<WordDto>();
        Assert.Equal("sommerfugl", fetched.Term);
    }

    [Fact]
    public async Task NewWords_AreServedNewestFirst()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        foreach (var (term, en) in new[] { ("en", "one"), ("to", "two"), ("tre", "three") })
            await CreateWordAsync(client, term, en);

        var served = new List<string>();
        var next = await GetAsync<StudyNextResponse>(client, "/api/study/next?exercise=TargetToKnown");
        while (next.Card is { IsNew: true } card)
        {
            served.Add(card.Prompt);
            next = await PostAsync<StudyNextResponse>(client, "/api/study/grade",
                new { wordId = card.Word.Id, exercise = "TargetToKnown", grade = "Good" });
        }
        Assert.Equal(["tre", "to", "en"], served);
    }

    [Fact]
    public async Task Relearn_MakesAStudiedWordDueNow_InBothDirections()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var word = await CreateWordAsync(client, "glemme", "to forget");
        foreach (var exercise in new[] { "TargetToKnown", "KnownToTarget" })
            await PostAsync<StudyNextResponse>(client, "/api/study/grade",
                new { wordId = word.Id, exercise, grade = "Good" });

        var before = await GetAsync<List<StudyCountsDto>>(client, "/api/study/counts");
        Assert.All(before, c => Assert.Equal(0, c.Due));

        var relearn = await client.PostAsync($"/api/words/{word.Id}/relearn", null);
        Assert.Equal(HttpStatusCode.NoContent, relearn.StatusCode);

        var after = await GetAsync<List<StudyCountsDto>>(client, "/api/study/counts");
        Assert.All(after, c => Assert.Equal(1, c.Due));
        Assert.All(after, c => Assert.Equal(0, c.New));

        // A review card again, back on the 1-day rung of the ladder (not a 0-day interval).
        var next = await GetAsync<StudyNextResponse>(client, "/api/study/next?exercise=TargetToKnown");
        Assert.NotNull(next.Card);
        Assert.Equal(word.Id, next.Card.Word.Id);
        Assert.False(next.Card.IsNew);
        Assert.Equal(new StudyIntervalsDto("10 min", "1 d", "1 d", "4 d"), next.Card.Intervals);

        // Grading it Good schedules it for tomorrow again.
        var graded = await PostAsync<StudyNextResponse>(client, "/api/study/grade",
            new { wordId = word.Id, exercise = "TargetToKnown", grade = "Good" });
        Assert.Null(graded.Card);
    }

    [Fact]
    public async Task Relearn_OfANeverStudiedWord_UsesTodaysNewAllowance()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        await UpdateSettingsAsync(client, dailyNewLimit: 1);
        var relearned = await CreateWordAsync(client, "vente", "to wait");
        await CreateWordAsync(client, "skynde", "to hurry"); // still new

        var relearn = await client.PostAsync($"/api/words/{relearned.Id}/relearn", null);
        Assert.Equal(HttpStatusCode.NoContent, relearn.StatusCode);

        var counts = await GetAsync<List<StudyCountsDto>>(client, "/api/study/counts");
        Assert.All(counts, c => Assert.Equal(1, c.Due));
        Assert.All(counts, c => Assert.Equal(0, c.New));
    }

    [Fact]
    public async Task Relearn_GivesANeverStudiedWordDueStates()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var word = await CreateWordAsync(client, "huske", "to remember");

        var relearn = await client.PostAsync($"/api/words/{word.Id}/relearn", null);
        Assert.Equal(HttpStatusCode.NoContent, relearn.StatusCode);

        var counts = await GetAsync<List<StudyCountsDto>>(client, "/api/study/counts");
        Assert.All(counts, c => Assert.Equal(1, c.Due));
        Assert.All(counts, c => Assert.Equal(0, c.New));

        var next = await GetAsync<StudyNextResponse>(client, "/api/study/next?exercise=KnownToTarget");
        Assert.NotNull(next.Card);
        Assert.Equal(word.Id, next.Card.Word.Id);
        Assert.False(next.Card.IsNew);
    }

    // --- helpers ---

    private static async Task<T> GetAsync<T>(HttpClient client, string url) =>
        await (await client.GetAsync(url)).ReadAsAsync<T>();

    private static async Task<T> PostAsync<T>(HttpClient client, string url, object body) =>
        await (await client.PostAsJsonAsync(url, body)).ReadAsAsync<T>();

    private static async Task<WordDto> CreateWordAsync(
        HttpClient client, string term, string enText, string[]? tags = null, string? ukText = null)
    {
        var translations = new List<object>
        {
            new { languageCode = "en", text = enText, exampleTranslation = (string?)null },
        };
        if (ukText is not null)
            translations.Add(new { languageCode = "uk", text = ukText, exampleTranslation = (string?)null });
        var response = await client.PostAsJsonAsync("/api/words",
            new { term, translations, tags = tags ?? [] });
        return await response.ReadAsAsync<WordDto>();
    }

    private static async Task UpdateSettingsAsync(
        HttpClient client, int dailyNewLimit = 20, string[]? knownLanguages = null)
    {
        var response = await client.PutAsJsonAsync("/api/settings", new
        {
            learningLanguage = "da",
            knownLanguages = knownLanguages ?? ["en"],
            dailyNewLimit,
        });
        response.EnsureSuccessStatusCode();
    }

    private record StudyCountsDto(string Exercise, int Due, int New, int ReviewedToday);
    private record StudyIntervalsDto(string Again, string Hard, string Good, string Easy);
    private record StudyCardDto(WordDto Word, string Prompt, string Answer, bool IsNew, StudyIntervalsDto Intervals);
    private record StudyNextResponse(StudyCardDto? Card, int Remaining);
}
