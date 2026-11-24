using System.Net;
using System.Net.Http.Json;
using AnkiLearner.Api.Contracts;
using AnkiLearner.Core.Srs;

namespace AnkiLearner.Tests.Integration;

public class TagsAndSettingsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Tags_CreateListRenameDelete_WithCounts()
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        var created = await (await client.PostAsJsonAsync("/api/tags", new { name = "verbs" }))
            .ReadAsAsync<TagDto>();
        Assert.Equal("verbs", created.Name);
        Assert.Equal(0, created.Count);

        // Attach the tag to a word → count reflects the link.
        await client.PostAsJsonAsync("/api/words", new
        {
            term = "løbe",
            translations = new[] { new { languageCode = "en", text = "to run", exampleTranslation = (string?)null } },
            tags = new[] { "verbs" },
        });
        var list = await (await client.GetAsync("/api/tags")).ReadAsAsync<List<TagDto>>();
        var verbs = Assert.Single(list, t => t.Name == "verbs");
        Assert.Equal(1, verbs.Count);

        var renamed = await (await client.PutAsJsonAsync($"/api/tags/{verbs.Id}", new { name = "verbs-da" }))
            .ReadAsAsync<TagDto>();
        Assert.Equal("verbs-da", renamed.Name);
        Assert.Equal(1, renamed.Count);

        // Deleting the tag unlinks it but keeps the word (spec FR-D6).
        var delete = await client.DeleteAsync($"/api/tags/{verbs.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        var words = await (await client.GetAsync("/api/words")).ReadAsAsync<PagedResponse<WordDto>>();
        Assert.Equal(1, words.Total);
        Assert.Empty(words.Items[0].Tags);
    }

    [Fact]
    public async Task Tags_DuplicateName_Returns409()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        await client.PostAsJsonAsync("/api/tags", new { name = "food" });
        var duplicate = await client.PostAsJsonAsync("/api/tags", new { name = "food" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task Tags_AreIsolatedPerUser()
    {
        var alice = await factory.CreateAuthenticatedClientAsync();
        var bob = await factory.CreateAuthenticatedClientAsync();
        var tag = await (await alice.PostAsJsonAsync("/api/tags", new { name = "private" }))
            .ReadAsAsync<TagDto>();

        var bobTags = await (await bob.GetAsync("/api/tags")).ReadAsAsync<List<TagDto>>();
        Assert.DoesNotContain(bobTags, t => t.Name == "private");
        Assert.Equal(HttpStatusCode.NotFound, (await bob.DeleteAsync($"/api/tags/{tag.Id}")).StatusCode);
    }

    [Fact]
    public async Task Settings_UpdateAndReadBack()
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        var updated = await (await client.PutAsJsonAsync("/api/settings",
                new
                {
                    learningLanguage = "da",
                    knownLanguages = new[] { "en", "uk" },
                    dailyNewLimit = 10,
                    maxIntervalDays = 365,
                }))
            .ReadAsAsync<SettingsDto>();
        Assert.Equal(["en", "uk"], updated.KnownLanguages);
        Assert.Equal(10, updated.DailyNewLimit);
        Assert.Equal(365, updated.MaxIntervalDays);

        var fetched = await (await client.GetAsync("/api/settings")).ReadAsAsync<SettingsDto>();
        Assert.Equal("da", fetched.LearningLanguage);
        Assert.Equal(["en", "uk"], fetched.KnownLanguages);
        Assert.Equal(365, fetched.MaxIntervalDays);
    }

    [Fact]
    public async Task Settings_MaxInterval_DefaultsAndSurvivesAnUpdateThatOmitsIt()
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        var initial = await (await client.GetAsync("/api/settings")).ReadAsAsync<SettingsDto>();
        Assert.Equal(Fsrs.DefaultMaxIntervalDays, initial.MaxIntervalDays);

        await client.PutAsJsonAsync("/api/settings",
            new { learningLanguage = "da", knownLanguages = new[] { "en" }, dailyNewLimit = 5, maxIntervalDays = 90 });

        // Older API clients omit the field entirely — that must not reset the user's choice.
        var afterOmitted = await (await client.PutAsJsonAsync("/api/settings",
                new { learningLanguage = "da", knownLanguages = new[] { "en" }, dailyNewLimit = 7 }))
            .ReadAsAsync<SettingsDto>();
        Assert.Equal(7, afterOmitted.DailyNewLimit);
        Assert.Equal(90, afterOmitted.MaxIntervalDays);
    }

    [Fact]
    public async Task Settings_FsrsRetentionAndWeights_DefaultSetAndReset()
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        var initial = await (await client.GetAsync("/api/settings")).ReadAsAsync<SettingsDto>();
        Assert.Equal(Fsrs.DefaultDesiredRetention, initial.DesiredRetention);
        Assert.Null(initial.FsrsWeights); // null = the defaults

        var weights = Fsrs.DefaultWeights.Select(w => Math.Round(w * 1.05, 4)).ToArray();
        weights[7] = 0.1; // keep every value inside its bounds
        var updated = await (await client.PutAsJsonAsync("/api/settings", new
            {
                learningLanguage = "da", knownLanguages = new[] { "en" }, dailyNewLimit = 20,
                desiredRetention = 0.85, fsrsWeights = weights,
            }))
            .ReadAsAsync<SettingsDto>();
        Assert.Equal(0.85, updated.DesiredRetention);
        Assert.Equal(weights, updated.FsrsWeights);

        // Omitted fields stay as they are.
        var omitted = await (await client.PutAsJsonAsync("/api/settings",
                new { learningLanguage = "da", knownLanguages = new[] { "en" }, dailyNewLimit = 15 }))
            .ReadAsAsync<SettingsDto>();
        Assert.Equal(0.85, omitted.DesiredRetention);
        Assert.Equal(weights, omitted.FsrsWeights);

        // An empty list goes back to the defaults.
        var reset = await (await client.PutAsJsonAsync("/api/settings", new
            {
                learningLanguage = "da", knownLanguages = new[] { "en" }, dailyNewLimit = 15,
                fsrsWeights = Array.Empty<double>(),
            }))
            .ReadAsAsync<SettingsDto>();
        Assert.Null(reset.FsrsWeights);

        var fetched = await (await client.GetAsync("/api/auth/me")).ReadAsAsync<MeResponse>();
        Assert.Equal(0.85, fetched.Settings.DesiredRetention);
        Assert.Null(fetched.Settings.FsrsWeights);
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(0.99)]
    public async Task Settings_RejectsRetentionOutOfRange(double retention)
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var response = await client.PutAsJsonAsync("/api/settings", new
        {
            learningLanguage = "da", knownLanguages = new[] { "en" }, dailyNewLimit = 20,
            desiredRetention = retention,
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Settings_RejectsInvalidWeights_AndKeepsTheOldOnes()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var saved = Fsrs.DefaultWeights.Select(w => Math.Round(w * 1.05, 4)).ToArray();
        saved[7] = 0.1;
        (await client.PutAsJsonAsync("/api/settings", new
        {
            learningLanguage = "da", knownLanguages = new[] { "en" }, dailyNewLimit = 20,
            fsrsWeights = saved,
        })).EnsureSuccessStatusCode();

        var tooFew = await client.PutAsJsonAsync("/api/settings", new
        {
            learningLanguage = "da", knownLanguages = new[] { "en" }, dailyNewLimit = 20,
            fsrsWeights = Fsrs.DefaultWeights.Take(19).ToArray(),
        });
        Assert.Equal(HttpStatusCode.BadRequest, tooFew.StatusCode);
        Assert.Contains("Expected 21 FSRS weights, got 19.", await tooFew.Content.ReadAsStringAsync());

        var outOfBounds = Fsrs.DefaultWeights.ToArray();
        outOfBounds[20] = 5; // decay must be ≤ 0.8
        var bad = await client.PutAsJsonAsync("/api/settings", new
        {
            learningLanguage = "da", knownLanguages = new[] { "en" }, dailyNewLimit = 20,
            fsrsWeights = outOfBounds,
        });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var fetched = await (await client.GetAsync("/api/settings")).ReadAsAsync<SettingsDto>();
        Assert.Equal(saved, fetched.FsrsWeights);
    }

    [Fact]
    public async Task Settings_RejectsInvalidLanguages()
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        var badCode = await client.PutAsJsonAsync("/api/settings",
            new { learningLanguage = "xx", knownLanguages = new[] { "en" }, dailyNewLimit = 20 });
        Assert.Equal(HttpStatusCode.BadRequest, badCode.StatusCode);

        var badKnown = await client.PutAsJsonAsync("/api/settings",
            new { learningLanguage = "da", knownLanguages = new[] { "zz" }, dailyNewLimit = 20 });
        Assert.Equal(HttpStatusCode.BadRequest, badKnown.StatusCode);

        var overlap = await client.PutAsJsonAsync("/api/settings",
            new { learningLanguage = "da", knownLanguages = new[] { "da" }, dailyNewLimit = 20 });
        Assert.Equal(HttpStatusCode.BadRequest, overlap.StatusCode);
    }

    [Fact]
    public async Task Languages_CatalogIsPublic()
    {
        var client = factory.CreateClient(); // no auth
        var languages = await (await client.GetAsync("/api/languages")).ReadAsAsync<List<Language>>();
        Assert.Contains(languages, l => l.Code == "da" && l.Name == "Danish");
        Assert.Contains(languages, l => l.Code == "uk");
    }

    private record Language(string Code, string Name);
}
