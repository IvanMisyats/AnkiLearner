using System.Net;
using System.Net.Http.Json;
using AnkiLearner.Api.Contracts;

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
                new { learningLanguage = "da", knownLanguages = new[] { "en", "uk" }, dailyNewLimit = 10 }))
            .ReadAsAsync<SettingsDto>();
        Assert.Equal(["en", "uk"], updated.KnownLanguages);
        Assert.Equal(10, updated.DailyNewLimit);

        var fetched = await (await client.GetAsync("/api/settings")).ReadAsAsync<SettingsDto>();
        Assert.Equal("da", fetched.LearningLanguage);
        Assert.Equal(["en", "uk"], fetched.KnownLanguages);
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
