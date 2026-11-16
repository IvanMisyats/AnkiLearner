using System.Text.Json;
using AnkiLearner.Core.Abstractions;
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
    public void BuildSystemPrompt_AsksForTheCitationForm_AndStillTheGender()
    {
        var prompt = ClaudeLookupProvider.BuildSystemPrompt("da", ["en"]);

        Assert.Contains(ClaudeLookupProvider.CitationFormRule("da"), prompt);
        Assert.Contains("citation form", prompt);
        Assert.Contains("\"en aftale\"", prompt);
        Assert.Contains("\"at spise\"", prompt);
        Assert.Contains("- gender:", prompt);
        Assert.Contains("word itself (no article or infinitive marker)", prompt);
        Assert.Contains("e.g. [ˈhunˀ]", prompt);
        Assert.Contains("(for example \"en\" or \"et\")", prompt);
        Assert.DoesNotContain("lemma", prompt);
    }

    [Theory]
    [InlineData("de")] // German has articles, but no particle table: duplicates match exactly
    [InlineData("sv")]
    public void BothPrompts_KeepThePlainLemma_ForLanguagesWithoutAParticleTable(string target)
    {
        foreach (var prompt in new[]
        {
            ClaudeLookupProvider.BuildSystemPrompt(target, ["en"]),
            ClaudeLookupProvider.BuildTranslateSystemPrompt(target, ["en"]),
        })
        {
            Assert.Contains("- term: the lemma (base/dictionary form)", prompt);
            Assert.DoesNotContain("dictionary citation form", prompt);
            Assert.DoesNotContain("indefinite article", prompt);
            Assert.DoesNotContain("en aftale", prompt);
            Assert.DoesNotContain("no article or infinitive marker", prompt);
            // No Danish examples outside Danish (the learner here knows only English).
            Assert.DoesNotContain("Danish", prompt);
            Assert.DoesNotContain("[ˈhunˀ]", prompt);
            Assert.DoesNotContain("\"et\"", prompt);
            Assert.DoesNotContain("\"barn\"", prompt);
            Assert.DoesNotContain("\"en gift\"", prompt);
            Assert.Contains("in square brackets", prompt);
        }
    }

    [Fact]
    public void BuildTranslateSystemPrompt_CoversLanguagesCapAndRules()
    {
        var prompt = ClaudeLookupProvider.BuildTranslateSystemPrompt("da", ["en", "uk"]);

        Assert.Contains("Danish dictionary and translator", prompt);
        Assert.Contains("knows: English, Ukrainian", prompt);
        Assert.Contains("da (Danish), en (English), uk (Ukrainian)", prompt);
        Assert.Contains("\"other\"", prompt);
        Assert.Contains("at most 5", prompt);
        Assert.Contains("prefer Danish", prompt);
        Assert.Contains("One card per citation form, listing all its senses in meanings", prompt);
        Assert.Contains("(like \"gift\" and \"en gift\")", prompt);
        Assert.Contains("then every input_translations value is \"\" and cards is empty", prompt);
        // One citation-form rule for both add paths.
        Assert.Contains(ClaudeLookupProvider.CitationFormRule("da"), prompt);
        Assert.Contains("\"\" when input_kind is \"sentence\"", prompt);
        Assert.Contains("note: one short sentence in English", prompt);
    }

    [Fact]
    public void BuildTranslateUserMessage_PutsAChosenSourceBeforeTheText()
    {
        Assert.Equal("hund", ClaudeLookupProvider.BuildTranslateUserMessage("hund", null));

        var message = ClaudeLookupProvider.BuildTranslateUserMessage("gift", "en");
        Assert.StartsWith("Source language: en (English), set by the user", message);
        Assert.EndsWith("\ngift", message);
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
        Assert.False(doc.RootElement.GetProperty("properties").TryGetProperty("note", out _));
    }

    [Fact]
    public void BuildTranslateSchema_HasLanguageEnumsAndCards()
    {
        using var doc = Parse(ClaudeLookupProvider.BuildTranslateSchema("da", ["en", "uk"]));
        var root = doc.RootElement;
        var properties = root.GetProperty("properties");

        Assert.Equal(["source_language", "input_kind", "input_translations", "cards"], Strings(root.GetProperty("required")));
        Assert.Equal(["da", "en", "uk", "other"], Strings(properties.GetProperty("source_language").GetProperty("enum")));
        Assert.Equal(["word", "phrase", "sentence"], Strings(properties.GetProperty("input_kind").GetProperty("enum")));
        Assert.Equal(["da", "en", "uk"], Strings(properties.GetProperty("input_translations").GetProperty("required")));

        var cards = properties.GetProperty("cards");
        Assert.Equal("array", cards.GetProperty("type").GetString());
        var card = cards.GetProperty("items");
        Assert.Equal(
            ["term", "transcription", "part_of_speech", "gender", "meanings", "example", "example_translations", "note"],
            Strings(card.GetProperty("required")));
        Assert.Equal(["en", "uk"], Strings(card.GetProperty("properties").GetProperty("meanings").GetProperty("required")));
    }

    [Fact]
    public void BuildTranslateSchema_IsIdenticalForTheSameLanguages() =>
        Assert.Equal(
            JsonSerializer.Serialize(ClaudeLookupProvider.BuildTranslateSchema("da", ["en", "uk"])),
            JsonSerializer.Serialize(ClaudeLookupProvider.BuildTranslateSchema("da", ["en", "uk"])));

    private static readonly string[] UnsupportedKeywords =
        ["minItems", "maxItems", "minLength", "maxLength", "minimum", "maximum"];

    [Theory]
    [InlineData("lookup", 3)]    // root card, meanings, example_translations
    [InlineData("translate", 5)] // root, input_translations, card, meanings, example_translations
    public void Schemas_CloseEveryObject_RequireEveryProperty_AndAvoidUnsupportedKeywords(
        string schema, int expectedObjects)
    {
        using var doc = Parse(schema == "lookup"
            ? ClaudeLookupProvider.BuildSchema(["en", "uk"])
            : ClaudeLookupProvider.BuildTranslateSchema("da", ["en", "uk"]));

        var objects = 0;
        void Walk(JsonElement element, string path)
        {
            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray()) Walk(item, path + "[]");
                return;
            }
            if (element.ValueKind != JsonValueKind.Object) return;

            foreach (var property in element.EnumerateObject())
                Assert.DoesNotContain(property.Name, UnsupportedKeywords);

            if (element.TryGetProperty("type", out var type) && type.GetString() == "object")
            {
                objects++;
                Assert.False(element.GetProperty("additionalProperties").GetBoolean(), path);
                Assert.Equal(
                    element.GetProperty("properties").EnumerateObject().Select(p => p.Name),
                    Strings(element.GetProperty("required")));
            }

            foreach (var property in element.EnumerateObject()) Walk(property.Value, $"{path}/{property.Name}");
        }

        Walk(doc.RootElement, "");
        Assert.Equal(expectedObjects, objects);
    }

    private static JsonDocument Parse(Dictionary<string, JsonElement> schema) =>
        JsonDocument.Parse(JsonSerializer.Serialize(schema));

    private static List<string> Strings(JsonElement array) =>
        [.. array.EnumerateArray().Select(e => e.GetString()!)];

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
    public void ParseResult_IgnoresNonStringMeanings()
    {
        const string json = """{ "term": "hund", "meanings": { "en": ["dog", 5, null, ""] } }""";
        Assert.Equal(["dog"], ClaudeLookupProvider.ParseResult(json, ["en"]).Meanings["en"]);
    }

    [Fact]
    public void ParseTranslateResult_MapsAllFields()
    {
        const string json = """
            {
              "source_language": "en",
              "input_kind": "word",
              "input_translations": { "da": "at vide", "en": "", "uk": "знати" },
              "cards": [
                {
                  "term": "at vide",
                  "transcription": "[ˈviːðə]",
                  "part_of_speech": "verb",
                  "gender": "",
                  "meanings": { "en": ["know (a fact)"], "uk": ["знати"] },
                  "example": "Jeg ved det.",
                  "example_translations": { "en": "I know it.", "uk": "Я це знаю." },
                  "note": "For facts; use kende for people and places."
                },
                {
                  "term": "at kende",
                  "transcription": "[ˈkɛnə]",
                  "part_of_speech": "verb",
                  "gender": "",
                  "meanings": { "en": ["know (be acquainted with)"], "uk": ["знати"] },
                  "example": "Jeg kender ham.",
                  "example_translations": { "en": "I know him.", "uk": "Я його знаю." },
                  "note": "For people and places."
                }
              ]
            }
            """;

        var result = ClaudeLookupProvider.ParseTranslateResult(json, "hund", "da", ["en", "uk"]);

        Assert.Equal("en", result.SourceLanguage);
        Assert.Equal(InputKind.Word, result.InputKind);
        Assert.Equal(new Dictionary<string, string> { ["da"] = "at vide", ["en"] = "", ["uk"] = "знати" },
            result.InputTranslations);
        Assert.Equal(["at vide", "at kende"], result.Cards.Select(c => c.Term));

        var card = result.Cards[0];
        Assert.Equal("[ˈviːðə]", card.Transcription);
        Assert.Equal("verb", card.PartOfSpeech);
        Assert.Equal("", card.Gender);
        Assert.Equal(["know (a fact)"], card.Meanings["en"]);
        Assert.Equal(["знати"], card.Meanings["uk"]);
        Assert.Equal("Jeg ved det.", card.Example);
        Assert.Equal("Я це знаю.", card.ExampleTranslations["uk"]);
        Assert.Equal("For facts; use kende for people and places.", card.Note);
    }

    [Fact]
    public void ParseTranslateResult_ToleratesMissingLanguagesAndFields()
    {
        const string json = """
            {
              "source_language": "da",
              "input_translations": { "en": "dog" },
              "cards": [ { "term": "hund", "meanings": { "en": ["dog"] } } ]
            }
            """;

        var result = ClaudeLookupProvider.ParseTranslateResult(json, "hund", "da", ["en", "uk"]);

        Assert.Equal(new Dictionary<string, string> { ["da"] = "", ["en"] = "dog", ["uk"] = "" },
            result.InputTranslations);
        var card = Assert.Single(result.Cards);
        Assert.Empty(card.Meanings["uk"]);
        Assert.Equal("", card.ExampleTranslations["en"]);
        Assert.Equal("", card.Note);
        Assert.Equal("", card.Transcription);
    }

    [Theory]
    [InlineData("""{ "source_language": "uk" }""", "uk")]
    [InlineData("""{ "source_language": "other" }""", "other")]
    [InlineData("""{ "source_language": "de" }""", "da")] // not one of the user's languages
    [InlineData("""{ "source_language": 7 }""", "da")]
    [InlineData("""{}""", "da")]
    public void ParseTranslateResult_UnknownOrMissingSource_IsTheTargetLanguage(string json, string expected) =>
        Assert.Equal(expected, ClaudeLookupProvider.ParseTranslateResult(json, "hund", "da", ["en", "uk"]).SourceLanguage);

    [Theory]
    [InlineData("word", "ondt i maven", InputKind.Word)]
    [InlineData("phrase", "hund", InputKind.Phrase)]
    [InlineData("sentence", "Tak!", InputKind.Sentence)]
    // An unknown kind falls back on the input: one token is a word, more is a phrase.
    [InlineData("paragraph", "hund", InputKind.Word)]
    [InlineData("paragraph", "ondt i maven", InputKind.Phrase)]
    [InlineData("", "hund", InputKind.Word)]
    public void ParseTranslateResult_MapsTheInputKind(string kind, string text, InputKind expected)
    {
        var json = $$"""{ "input_kind": "{{kind}}" }""";
        Assert.Equal(expected, ClaudeLookupProvider.ParseTranslateResult(json, text, "da", ["en"]).InputKind);
    }

    [Fact]
    public void ParseTranslateResult_KeepsEveryCardObject_AndSkipsOnlyNonObjects()
    {
        // Empty terms and the 5-card cap are TranslateShaper's job, not the parser's.
        var cards = string.Join(", ", Enumerable.Range(1, 6).Select(i => $$"""{ "term": "t{{i}}" }"""));
        var json = $$"""{ "cards": [ {{cards}}, { "term": "" }, "junk", 3, null ] }""";

        var result = ClaudeLookupProvider.ParseTranslateResult(json, "tal", "da", ["en"]);

        Assert.Equal(["t1", "t2", "t3", "t4", "t5", "t6", ""], result.Cards.Select(c => c.Term));
    }

    [Fact]
    public void ParseTranslateResult_RejectsANonObjectRoot() =>
        Assert.Throws<InvalidOperationException>(
            () => ClaudeLookupProvider.ParseTranslateResult("[]", "hund", "da", ["en"]));

    [Theory]
    [InlineData("{ \"cards\": [ { \"term\": \"secret model output")]
    [InlineData("not json at all")]
    public void Parsers_ReplaceMalformedJson_WithoutQuotingIt(string json)
    {
        // A JsonException message can quote the model's output, and failures are logged.
        var translate = Assert.Throws<InvalidOperationException>(
            () => ClaudeLookupProvider.ParseTranslateResult(json, "hund", "da", ["en"]));
        var lookup = Assert.Throws<InvalidOperationException>(
            () => ClaudeLookupProvider.ParseResult(json, ["en"]));

        foreach (var ex in new[] { translate, lookup })
        {
            Assert.Equal("The provider returned malformed JSON.", ex.Message);
            Assert.Null(ex.InnerException);
        }
    }

    [Theory]
    [InlineData("max_tokens")]
    [InlineData("refusal")]
    public void EnsureCompleted_ThrowsOnATruncatedOrRefusedAnswer(string stopReason)
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => ClaudeLookupProvider.EnsureCompleted(stopReason, 4096));
        Assert.Contains(stopReason, ex.Message);
        Assert.Contains("4096", ex.Message);
    }

    [Theory]
    [InlineData("end_turn")]
    [InlineData(null)]
    public void EnsureCompleted_AcceptsACompleteAnswer(string? stopReason) =>
        ClaudeLookupProvider.EnsureCompleted(stopReason, 100);

    [Fact]
    public async Task WithTimeoutAsync_ReturnsTheResult() =>
        Assert.Equal(42, await ClaudeLookupProvider.WithTimeoutAsync(
            _ => Task.FromResult(42), TimeSpan.FromSeconds(5), CancellationToken.None));

    [Fact]
    public async Task WithTimeoutAsync_RunningOutOfTime_IsATimeoutException()
    {
        await Assert.ThrowsAsync<TimeoutException>(() => ClaudeLookupProvider.WithTimeoutAsync(
            async token =>
            {
                await Task.Delay(Timeout.Infinite, token);
                return 0;
            },
            TimeSpan.FromMilliseconds(50),
            CancellationToken.None));
    }

    [Fact]
    public async Task WithTimeoutAsync_CallerCancellation_StaysACancellation()
    {
        using var caller = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ClaudeLookupProvider.WithTimeoutAsync(
            async token =>
            {
                await Task.Delay(Timeout.Infinite, token);
                return 0;
            },
            TimeSpan.FromMinutes(1),
            caller.Token));
        Assert.True(caller.IsCancellationRequested);
    }

    [Fact]
    public void Options_Defaults()
    {
        var options = new AnthropicOptions();
        Assert.Equal(4096, options.MaxTokens);
        Assert.Equal(30, options.TimeoutSeconds);
    }

    [Fact]
    public async Task Provider_WithoutApiKey_IsUnavailable()
    {
        var provider = new ClaudeLookupProvider(Options.Create(new AnthropicOptions { ApiKey = "" }));

        Assert.False(provider.IsAvailable);
        Assert.Equal("Claude (claude-haiku-4-5)", provider.Name);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.LookupAsync("hund", "da", ["en"], CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.TranslateAsync("hund", null, "da", ["en"], CancellationToken.None));
    }
}
