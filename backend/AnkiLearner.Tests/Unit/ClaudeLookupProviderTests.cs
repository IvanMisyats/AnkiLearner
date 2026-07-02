using System.Text.Json;
using AnkiLearner.Infrastructure.Lookup;
using Microsoft.Extensions.Options;

namespace AnkiLearner.Tests.Unit;

public class ClaudeLookupProviderTests
{
    [Fact]
    public void BuildSystemPrompt_UsesLanguageNamesAndCodes()
    {
        var prompt = ClaudeLookupProvider.BuildSystemPrompt("da", ["en", "uk"]);

        Assert.Contains("Danish dictionary", prompt);
        Assert.Contains("English, Ukrainian", prompt);
        Assert.Contains("en, uk", prompt);
    }

    [Fact]
    public void BuildSchema_ContainsOneEntryPerKnownLanguage()
    {
        var schema = ClaudeLookupProvider.BuildSchema(["en", "uk"]);
        var json = JsonSerializer.Serialize(schema);
        using var doc = JsonDocument.Parse(json);

        var meanings = doc.RootElement.GetProperty("properties").GetProperty("meanings");
        Assert.True(meanings.GetProperty("properties").TryGetProperty("en", out _));
        Assert.True(meanings.GetProperty("properties").TryGetProperty("uk", out _));
        Assert.Equal(2, meanings.GetProperty("required").GetArrayLength());
        Assert.False(meanings.GetProperty("additionalProperties").GetBoolean());

        var translations = doc.RootElement.GetProperty("properties").GetProperty("example_translations");
        Assert.True(translations.GetProperty("properties").TryGetProperty("uk", out _));
    }

    [Fact]
    public void ParseResult_MapsAllFields()
    {
        const string json = """
            {
              "term": "hund",
              "transcription": "[ˈhunˀ]",
              "part_of_speech": "noun",
              "gender": "en",
              "meanings": { "en": ["dog", "hound"], "uk": ["пес", "собака"] },
              "example": "Hunden løber i haven.",
              "example_translations": { "en": "The dog runs in the garden.", "uk": "Пес бігає в саду." }
            }
            """;

        var result = ClaudeLookupProvider.ParseResult(json, ["en", "uk"]);

        Assert.Equal("hund", result.Term);
        Assert.Equal("[ˈhunˀ]", result.Transcription);
        Assert.Equal("noun", result.PartOfSpeech);
        Assert.Equal("en", result.Gender);
        Assert.Equal(["dog", "hound"], result.Meanings["en"]);
        Assert.Equal(["пес", "собака"], result.Meanings["uk"]);
        Assert.Equal("Hunden løber i haven.", result.Example);
        Assert.Equal("Пес бігає в саду.", result.ExampleTranslations["uk"]);
    }

    [Fact]
    public void ParseResult_ToleratesMissingLanguageEntries()
    {
        const string json = """{ "term": "hund", "meanings": { "en": ["dog"] } }""";

        var result = ClaudeLookupProvider.ParseResult(json, ["en", "uk"]);

        Assert.Equal(["dog"], result.Meanings["en"]);
        Assert.Empty(result.Meanings["uk"]);
        Assert.Equal(string.Empty, result.ExampleTranslations["uk"]);
        Assert.Equal(string.Empty, result.Transcription);
    }

    [Fact]
    public async Task Provider_WithoutApiKey_IsUnavailable()
    {
        var provider = new ClaudeLookupProvider(Options.Create(new AnthropicOptions { ApiKey = "" }));

        Assert.False(provider.IsAvailable);
        Assert.Equal("Claude (claude-haiku-4-5)", provider.Name);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.LookupAsync("hund", "da", ["en"], CancellationToken.None));
    }
}
