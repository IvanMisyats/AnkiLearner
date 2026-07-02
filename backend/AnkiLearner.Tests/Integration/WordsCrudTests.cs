using System.Net;
using System.Net.Http.Json;
using AnkiLearner.Api.Contracts;

namespace AnkiLearner.Tests.Integration;

public class WordsCrudTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Create_Get_Update_Delete_FullCrud()
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        var create = await client.PostAsJsonAsync("/api/words", new
        {
            term = "hund",
            transcription = "[ˈhunˀ]",
            partOfSpeech = "noun",
            gender = "en",
            example = "Hunden løber.",
            translations = new[] { new { languageCode = "en", text = "dog", exampleTranslation = "The dog runs." } },
            tags = new[] { "animals" },
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var word = await create.ReadAsAsync<WordDto>();
        Assert.Equal("hund", word.Term);
        Assert.Equal("da", word.LanguageCode);
        Assert.Single(word.Translations);
        Assert.Equal("dog", word.Translations[0].Text);
        Assert.Equal(["animals"], word.Tags);

        var fetched = await (await client.GetAsync($"/api/words/{word.Id}")).ReadAsAsync<WordDto>();
        Assert.Equal(word.Id, fetched.Id);

        var update = await client.PutAsJsonAsync($"/api/words/{word.Id}", new
        {
            term = "hund",
            translations = new[] { new { languageCode = "en", text = "dog, hound", exampleTranslation = (string?)null } },
            tags = new[] { "animals", "pets" },
        });
        var updated = await update.ReadAsAsync<WordDto>();
        Assert.Equal("dog, hound", updated.Translations[0].Text);
        Assert.Equal(["animals", "pets"], updated.Tags);

        var delete = await client.DeleteAsync($"/api/words/{word.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/words/{word.Id}")).StatusCode);
    }

    [Fact]
    public async Task List_SupportsSearchAndTagFilter()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        await CreateWordAsync(client, "kat", "cat", tags: ["animals"]);
        await CreateWordAsync(client, "hus", "house", tags: ["buildings"]);
        await CreateWordAsync(client, "havekat", "garden cat", tags: []);

        var all = await (await client.GetAsync("/api/words")).ReadAsAsync<PagedResponse<WordDto>>();
        Assert.Equal(3, all.Total);

        // Search matches the target term…
        var byTerm = await (await client.GetAsync("/api/words?search=kat")).ReadAsAsync<PagedResponse<WordDto>>();
        Assert.Equal(2, byTerm.Total);

        // …and translation text.
        var byTranslation = await (await client.GetAsync("/api/words?search=house")).ReadAsAsync<PagedResponse<WordDto>>();
        Assert.Equal(1, byTranslation.Total);
        Assert.Equal("hus", byTranslation.Items[0].Term);

        var byTag = await (await client.GetAsync("/api/words?tag=animals")).ReadAsAsync<PagedResponse<WordDto>>();
        Assert.Equal(1, byTag.Total);
        Assert.Equal("kat", byTag.Items[0].Term);

        var byTags = await (await client.GetAsync("/api/words?tag=animals,buildings")).ReadAsAsync<PagedResponse<WordDto>>();
        Assert.Equal(2, byTags.Total);
    }

    [Fact]
    public async Task Words_AreIsolatedPerUser()
    {
        var alice = await factory.CreateAuthenticatedClientAsync();
        var bob = await factory.CreateAuthenticatedClientAsync();

        var word = await CreateWordAsync(alice, "fugl", "bird");

        var bobList = await (await bob.GetAsync("/api/words")).ReadAsAsync<PagedResponse<WordDto>>();
        Assert.Equal(0, bobList.Total);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/words/{word.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await bob.DeleteAsync($"/api/words/{word.Id}")).StatusCode);
    }

    [Fact]
    public async Task Html_IsSanitizedOnSave()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var create = await client.PostAsJsonAsync("/api/words", new
        {
            term = "<b>farlig</b><script>alert('x')</script>",
            notes = "<i>ok</i><img src=x onerror=alert(1)>",
            translations = new[] { new { languageCode = "en", text = "dangerous<script>bad()</script>", exampleTranslation = (string?)null } },
        });
        var word = await create.ReadAsAsync<WordDto>();
        Assert.Contains("<b>farlig</b>", word.Term);
        Assert.DoesNotContain("script", word.Term, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onerror", word.Notes ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("script", word.Translations[0].Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ChangingLearningLanguage_HidesWordsWithoutDeleting()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        await CreateWordAsync(client, "bord", "table");

        // Switch learning language da → de: dictionary appears empty.
        await UpdateSettingsAsync(client, "de", ["en"]);
        var hidden = await (await client.GetAsync("/api/words")).ReadAsAsync<PagedResponse<WordDto>>();
        Assert.Equal(0, hidden.Total);

        // Switch back: the word is still there.
        await UpdateSettingsAsync(client, "da", ["en"]);
        var restored = await (await client.GetAsync("/api/words")).ReadAsAsync<PagedResponse<WordDto>>();
        Assert.Equal(1, restored.Total);
    }

    [Fact]
    public async Task DuplicateCheck_MatchesNormalizedTerm()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var word = await CreateWordAsync(client, "<b>Hest</b>", "horse");

        var check = await (await client.GetAsync("/api/words/duplicate?term=%20hest%20"))
            .ReadAsAsync<DuplicateCheckResponse>();
        Assert.True(check.Exists);
        Assert.Equal(word.Id, check.WordId);

        // Excluding the word itself (edit form) finds no duplicate.
        var excluded = await (await client.GetAsync($"/api/words/duplicate?term=hest&excludeId={word.Id}"))
            .ReadAsAsync<DuplicateCheckResponse>();
        Assert.False(excluded.Exists);
    }

    [Fact]
    public async Task Translation_InUnknownLanguage_IsRejected()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync("/api/words", new
        {
            term = "vindue",
            translations = new[] { new { languageCode = "fr", text = "fenêtre", exampleTranslation = (string?)null } },
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task HiddenLanguageTranslations_SurviveWordUpdates()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        await UpdateSettingsAsync(client, "da", ["en", "uk"]);
        var word = await CreateWordAsync(client, "æble", "apple", ukText: "яблуко");

        // User temporarily removes Ukrainian from known languages and edits the word.
        await UpdateSettingsAsync(client, "da", ["en"]);
        await client.PutAsJsonAsync($"/api/words/{word.Id}", new
        {
            term = "æble",
            translations = new[] { new { languageCode = "en", text = "apple (fruit)", exampleTranslation = (string?)null } },
        });

        // Re-adding Ukrainian restores the preserved translation (spec FR-S6).
        await UpdateSettingsAsync(client, "da", ["en", "uk"]);
        var restored = await (await client.GetAsync($"/api/words/{word.Id}")).ReadAsAsync<WordDto>();
        Assert.Contains(restored.Translations, t => t.LanguageCode == "uk" && t.Text == "яблуко");
        Assert.Contains(restored.Translations, t => t.LanguageCode == "en" && t.Text == "apple (fruit)");
    }

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

    private static async Task UpdateSettingsAsync(HttpClient client, string learning, string[] known)
    {
        var response = await client.PutAsJsonAsync("/api/settings",
            new { learningLanguage = learning, knownLanguages = known, dailyNewLimit = 20 });
        response.EnsureSuccessStatusCode();
    }
}
