using System.Net;
using System.Net.Http.Json;
using AnkiLearner.Api.Contracts;
using AnkiLearner.Core.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AnkiLearner.Tests.Integration;

public class TranslateEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    /// <summary>A client on a host using <paramref name="provider"/>, signed in as a new user
    /// (default settings: learning Danish, knowing English).</summary>
    private async Task<HttpClient> CreateClientAsync(
        IWordLookupProvider provider, bool authenticate = true, int? lookupPerMinute = null)
    {
        var client = factory.WithWebHostBuilder(builder =>
        {
            if (lookupPerMinute is not null)
                builder.UseSetting("RateLimiting:LookupPerMinute", lookupPerMinute.ToString());
            builder.ConfigureServices(services => services.Replace(ServiceDescriptor.Singleton(provider)));
        }).CreateClient();
        if (!authenticate) return client;

        var email = $"user-{Guid.NewGuid():N}@test.local";
        var response = await client.PostAsJsonAsync("/api/auth/register", new { email, password = "password123" });
        var auth = await response.ReadAsAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new("Bearer", auth.AccessToken);
        return client;
    }

    private static Task<HttpResponseMessage> TranslateAsync(HttpClient client, string text, string? sourceLanguage = null) =>
        client.PostAsJsonAsync("/api/translate", new { text, sourceLanguage });

    private static TranslateResult Result(
        string source, InputKind kind, Dictionary<string, string> inputTranslations, params string[] terms) =>
        new(source, kind, inputTranslations,
            [.. terms.Select(t => new TranslateCard(t, "", "noun", "", new() { ["en"] = ["meaning"] },
                "Model example.", new() { ["en"] = "Model example translation." }, ""))]);

    [Fact]
    public async Task Translate_RequiresAuthentication()
    {
        var client = await CreateClientAsync(new FakeLookupProvider(available: true), authenticate: false);
        var response = await TranslateAsync(client, "hund");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Translate_WhenUnavailable_Returns503()
    {
        var client = await CreateClientAsync(new FakeLookupProvider(available: false));
        var response = await TranslateAsync(client, "hund");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Translate_WhenProviderFails_Returns502()
    {
        var client = await CreateClientAsync(new FakeLookupProvider(available: true, throws: true));
        var response = await TranslateAsync(client, "hund");
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Theory]
    [InlineData(typeof(TimeoutException))]
    // A cancellation the caller did not ask for is a provider failure, not an aborted request.
    [InlineData(typeof(OperationCanceledException))]
    public async Task Translate_WhenProviderTimesOutOrCancelsItself_Returns502(Type exceptionType)
    {
        var provider = new FakeLookupProvider(available: true)
        {
            OnTranslate = (_, _) => throw (Exception)Activator.CreateInstance(exceptionType)!,
        };
        var client = await CreateClientAsync(provider);

        var response = await TranslateAsync(client, "hund");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact]
    public async Task Translate_RejectsBadInput_WithoutCallingTheProvider()
    {
        var provider = new FakeLookupProvider(available: true);
        var client = await CreateClientAsync(provider);

        Assert.Equal(HttpStatusCode.BadRequest, (await TranslateAsync(client, "")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await TranslateAsync(client, " \n\t  ")).StatusCode);
        // Invisible characters only: empty once cleaned.
        Assert.Equal(HttpStatusCode.BadRequest, (await TranslateAsync(client, "\u200B\uFEFF\u200D\u00AD")).StatusCode);
        // Over 300 characters once cleaned.
        Assert.Equal(HttpStatusCode.BadRequest, (await TranslateAsync(client, new string('a', 301))).StatusCode);
        // Over the 1000-character raw cap, although only "hund" once cleaned.
        Assert.Equal(HttpStatusCode.BadRequest, (await TranslateAsync(client, new string(' ', 700) + "hund" + new string(' ', 297))).StatusCode);
        // German is neither the learning language nor a known language of this user.
        Assert.Equal(HttpStatusCode.BadRequest, (await TranslateAsync(client, "Hund", "de")).StatusCode);

        Assert.Null(provider.LastTranslateText);
        // 300 characters is still fine.
        Assert.Equal(HttpStatusCode.OK, (await TranslateAsync(client, new string('a', 300))).StatusCode);
    }

    [Fact]
    public async Task Translate_TheLengthLimitAppliesToTheCleanedText()
    {
        var provider = new FakeLookupProvider(available: true);
        var client = await CreateClientAsync(provider);
        // A pasted text with line breaks: 317 characters raw, 289 once CRLFs become spaces.
        var text = string.Join("\r\n", Enumerable.Repeat("abcdefghi", 29));
        Assert.True(text.Length > 300);

        var response = await TranslateAsync(client, text);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(string.Join(" ", Enumerable.Repeat("abcdefghi", 29)), provider.LastTranslateText);
    }

    [Fact]
    public async Task Translate_UnsupportedLanguage_Returns422_NamingTheSupportedOnes()
    {
        var provider = new FakeLookupProvider(available: true)
        {
            OnTranslate = (_, _) => Result("other", InputKind.Phrase, [], "god dag"),
        };
        var client = await CreateClientAsync(provider);

        var response = await TranslateAsync(client, "guten Tag");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("Only Danish and English are supported.", problem!.Title);
    }

    [Fact]
    public async Task Translate_OverrideWinsOverOther_AndAnswers200()
    {
        var provider = new FakeLookupProvider(available: true)
        {
            OnTranslate = (_, _) => Result("other", InputKind.Word, [], "en gave"),
        };
        var client = await CreateClientAsync(provider);

        var response = await TranslateAsync(client, "gift", "en");

        var result = await response.ReadAsAsync<ResponseBody>();
        Assert.Equal("en", result.SourceLanguage);
        Assert.Equal("en gave", Assert.Single(result.Cards).Term);
    }

    [Fact]
    public async Task Translate_SourceOverride_ReachesTheProvider_AndIsEchoed()
    {
        // The fake "detects" Danish whatever it is told.
        var provider = new FakeLookupProvider(available: true)
        {
            OnTranslate = (_, _) => Result("da", InputKind.Word, [], "en gave"),
        };
        var client = await CreateClientAsync(provider);

        var overridden = await (await TranslateAsync(client, "gift", "en")).ReadAsAsync<ResponseBody>();
        Assert.Equal("en", provider.LastTranslateSource);
        Assert.Equal("en", overridden.SourceLanguage);
        Assert.Equal("gift", overridden.InputTranslations["en"]);

        var auto = await (await TranslateAsync(client, "gift")).ReadAsAsync<ResponseBody>();
        Assert.Null(provider.LastTranslateSource);
        Assert.Equal("da", auto.SourceLanguage);

        await TranslateAsync(client, "gift", "");
        Assert.Null(provider.LastTranslateSource);

        // Codes are case-insensitive.
        var upper = await (await TranslateAsync(client, "gift", "EN")).ReadAsAsync<ResponseBody>();
        Assert.Equal("en", provider.LastTranslateSource);
        Assert.Equal("en", upper.SourceLanguage);
    }

    [Fact]
    public async Task Translate_FlagsExistingWords_WhenOnlyTheArticleDiffers()
    {
        var provider = new FakeLookupProvider(available: true)
        {
            OnTranslate = (_, _) => Result("da", InputKind.Word, [], "aftale", "at spise", "hund"),
        };
        var client = await CreateClientAsync(provider);
        var noun = await CreateWordAsync(client, "en aftale");
        var verb = await CreateWordAsync(client, "spise");

        var result = await (await TranslateAsync(client, "aftale")).ReadAsAsync<ResponseBody>();

        Assert.Equal(noun.Id, result.Cards.Single(c => c.Term == "aftale").ExistingWordId);
        Assert.Equal(verb.Id, result.Cards.Single(c => c.Term == "at spise").ExistingWordId);
        Assert.Null(result.Cards.Single(c => c.Term == "hund").ExistingWordId);
    }

    [Theory]
    [InlineData("en gift")] // only the poison is saved: "gift" (married) must stay addable
    [InlineData("gift")]    // only "married" is saved: "en gift" (poison) must stay addable
    public async Task Translate_AWordSavedForOneHomograph_IsNotFlaggedOnTheOther(string saved)
    {
        var provider = new FakeLookupProvider(available: true)
        {
            OnTranslate = (_, _) => Result("da", InputKind.Word, [], "gift", "en gift"),
        };
        var client = await CreateClientAsync(provider);
        var word = await CreateWordAsync(client, saved);

        var result = await (await TranslateAsync(client, "gift")).ReadAsAsync<ResponseBody>();

        Assert.Equal(word.Id, result.Cards.Single(c => c.Term == saved).ExistingWordId);
        Assert.Null(result.Cards.Single(c => c.Term != saved).ExistingWordId);
    }

    [Fact]
    public async Task Translate_PrefersTheExactMatch()
    {
        var provider = new FakeLookupProvider(available: true)
        {
            OnTranslate = (_, _) => Result("da", InputKind.Word, [], "gift", "en gift"),
        };
        var client = await CreateClientAsync(provider);
        var noun = await CreateWordAsync(client, "en gift");
        var adjective = await CreateWordAsync(client, "gift");

        var result = await (await TranslateAsync(client, "gift")).ReadAsAsync<ResponseBody>();

        Assert.Equal(adjective.Id, result.Cards.Single(c => c.Term == "gift").ExistingWordId);
        Assert.Equal(noun.Id, result.Cards.Single(c => c.Term == "en gift").ExistingWordId);
    }

    [Fact]
    public async Task Translate_NeverFlagsAnotherUsersWord_OrAWordInAnotherLearningLanguage()
    {
        var provider = new FakeLookupProvider(available: true)
        {
            OnTranslate = (_, _) => Result("da", InputKind.Word, [], "aftale"),
        };
        var client = await CreateClientAsync(provider);
        var otherUser = await factory.CreateAuthenticatedClientAsync();
        await CreateWordAsync(otherUser, "en aftale");

        // The same user's word, saved while learning Swedish.
        await UpdateSettingsAsync(client, "sv");
        await CreateWordAsync(client, "aftale");
        await UpdateSettingsAsync(client, "da");

        var result = await (await TranslateAsync(client, "aftale")).ReadAsAsync<ResponseBody>();

        Assert.Null(Assert.Single(result.Cards).ExistingWordId);
    }

    [Fact]
    public async Task Translate_Sentence_UsesTheInputAsExample_AndOffersTheWholeInput()
    {
        var provider = new FakeLookupProvider(available: true)
        {
            OnTranslate = (_, _) => Result("da", InputKind.Sentence,
                new() { ["en"] = "I have a stomach ache." }, "ondt i maven", "en mave"),
        };
        var client = await CreateClientAsync(provider);

        var result = await (await TranslateAsync(client, "  Jeg har  ondt\ni maven ")).ReadAsAsync<ResponseBody>();

        const string cleaned = "Jeg har ondt i maven";
        Assert.Equal(cleaned, provider.LastTranslateText);
        Assert.Equal("Sentence", result.InputKind);
        Assert.Equal(cleaned, result.InputTranslations["da"]);
        Assert.Equal(2, result.Cards.Count);
        Assert.All(result.Cards, card =>
        {
            Assert.Equal(cleaned, card.Example);
            Assert.Equal("I have a stomach ache.", card.ExampleTranslations["en"]);
        });

        Assert.NotNull(result.WholeInput);
        Assert.Equal(cleaned, result.WholeInput.Term);
        Assert.Equal(["I have a stomach ache."], result.WholeInput.Meanings["en"]);
        Assert.Null(result.WholeInput.ExistingWordId);
    }

    [Fact]
    public async Task Translate_FlagsTheWholeInput_WhenItIsAlreadySaved()
    {
        var provider = new FakeLookupProvider(available: true)
        {
            OnTranslate = (_, _) => Result("da", InputKind.Phrase, new() { ["en"] = "break up with" }, "at slå op"),
        };
        var client = await CreateClientAsync(provider);
        var saved = await CreateWordAsync(client, "slå op med");

        var result = await (await TranslateAsync(client, "slå op med")).ReadAsAsync<ResponseBody>();

        Assert.Equal(saved.Id, result.WholeInput?.ExistingWordId);
    }

    [Fact]
    public async Task Translate_WholePhrase_DropsTrailingPunctuation_AndFindsTheSavedEntry()
    {
        var provider = new FakeLookupProvider(available: true)
        {
            OnTranslate = (_, _) => Result("da", InputKind.Phrase, new() { ["en"] = "Thanks for the meal!" }, "en mad"),
        };
        var client = await CreateClientAsync(provider);
        var saved = await CreateWordAsync(client, "tak for mad");

        var result = await (await TranslateAsync(client, "Tak for mad!")).ReadAsAsync<ResponseBody>();

        Assert.Equal("Tak for mad", result.WholeInput?.Term);
        Assert.Equal(saved.Id, result.WholeInput?.ExistingWordId);
    }

    [Theory]
    [InlineData("Jeg har ondt i maven")]  // saved without the full stop
    [InlineData("Jeg har ondt i maven.")] // saved as typed
    public async Task Translate_WholeSentence_KeepsItsPunctuation_AndFindsTheSavedEntryEitherWay(string savedTerm)
    {
        var provider = new FakeLookupProvider(available: true)
        {
            OnTranslate = (_, _) => Result("da", InputKind.Sentence, new() { ["en"] = "I have a stomach ache." }, "en mave"),
        };
        var client = await CreateClientAsync(provider);
        var saved = await CreateWordAsync(client, savedTerm);

        var result = await (await TranslateAsync(client, "Jeg har ondt i maven.")).ReadAsAsync<ResponseBody>();

        Assert.Equal("Jeg har ondt i maven.", result.WholeInput?.Term);
        Assert.Equal(saved.Id, result.WholeInput?.ExistingWordId);
    }

    [Fact]
    public async Task Translate_SharesTheLookupRateLimit()
    {
        var client = await CreateClientAsync(new FakeLookupProvider(available: true), lookupPerMinute: 2);

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/lookup", new { term = "hund" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/lookup", new { term = "kat" })).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await TranslateAsync(client, "hest")).StatusCode);
    }

    private static async Task<WordDto> CreateWordAsync(HttpClient client, string term)
    {
        var response = await client.PostAsJsonAsync("/api/words", new
        {
            term,
            translations = new[] { new { languageCode = "en", text = "x", exampleTranslation = (string?)null } },
        });
        return await response.ReadAsAsync<WordDto>();
    }

    private static async Task UpdateSettingsAsync(HttpClient client, string learning)
    {
        var response = await client.PutAsJsonAsync("/api/settings",
            new { learningLanguage = learning, knownLanguages = new[] { "en" }, dailyNewLimit = 20 });
        response.EnsureSuccessStatusCode();
    }

    // The wire format; InputKind arrives as its PascalCase name.
    private record CardBody(
        string Term, string Transcription, string PartOfSpeech, string Gender,
        Dictionary<string, List<string>> Meanings, string Example,
        Dictionary<string, string> ExampleTranslations, string Note, Guid? ExistingWordId);

    private record ResponseBody(
        string SourceLanguage, string InputKind, Dictionary<string, string> InputTranslations,
        List<CardBody> Cards, CardBody? WholeInput);
}
