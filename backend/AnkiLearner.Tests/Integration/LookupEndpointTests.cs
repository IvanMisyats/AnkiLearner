using System.Net;
using System.Net.Http.Json;
using AnkiLearner.Core.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AnkiLearner.Tests.Integration;

/// <summary>Deterministic stand-in so tests never call the real Claude API.</summary>
public class FakeLookupProvider(bool available, bool throws = false) : IWordLookupProvider
{
    public string Name => "Fake";
    public bool IsAvailable => available;

    /// <summary>Builds the raw translate result from (text, sourceLanguage). Defaults to one
    /// Danish card whose term is the text.</summary>
    public Func<string, string?, TranslateResult> OnTranslate { get; set; } = (text, source) =>
        new TranslateResult(
            source ?? "da",
            InputKind.Word,
            new Dictionary<string, string> { ["en"] = $"translation-of-{text}" },
            [
                new TranslateCard(
                    text, "[ˈtɛst]", "noun", "en",
                    new Dictionary<string, List<string>> { ["en"] = ["meaning-en"] },
                    $"Example with {text}.",
                    new Dictionary<string, string> { ["en"] = "translation-en" },
                    "A note."),
            ]);

    /// <summary>The arguments of the last TranslateAsync call.</summary>
    public string? LastTranslateText { get; private set; }
    public string? LastTranslateSource { get; private set; }

    public Task<TranslateResult> TranslateAsync(
        string text, string? sourceLanguage, string targetLanguage, IReadOnlyList<string> knownLanguages,
        CancellationToken ct)
    {
        LastTranslateText = text;
        LastTranslateSource = sourceLanguage;
        if (throws) throw new InvalidOperationException("Simulated provider failure.");
        return Task.FromResult(OnTranslate(text, sourceLanguage));
    }

    /// <summary>When set, LookupAsync throws this instead of answering.</summary>
    public Exception? LookupFailure { get; init; }

    public Task<WordLookupResult> LookupAsync(
        string term, string targetLanguage, IReadOnlyList<string> knownLanguages, CancellationToken ct)
    {
        if (LookupFailure is not null) throw LookupFailure;
        if (throws) throw new InvalidOperationException("Simulated provider failure.");
        return Task.FromResult(new WordLookupResult(
            term,
            "[ˈtɛst]",
            "noun",
            "en",
            knownLanguages.ToDictionary(c => c, c => new List<string> { $"meaning-{c}" }),
            $"Example with {term}.",
            knownLanguages.ToDictionary(c => c, c => $"translation-{c}")));
    }
}

public class LookupEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private HttpClient CreateClientWithProvider(IWordLookupProvider provider) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.Replace(ServiceDescriptor.Singleton(provider)))).CreateClient();

    private static async Task AuthenticateAsync(HttpClient client)
    {
        var email = $"user-{Guid.NewGuid():N}@test.local";
        var response = await client.PostAsJsonAsync("/api/auth/register",
            new { email, password = "password123" });
        var auth = await response.ReadAsAsync<AnkiLearner.Api.Contracts.AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new("Bearer", auth.AccessToken);
    }

    [Fact]
    public async Task Status_ReportsUnavailable_WithoutKey()
    {
        var client = CreateClientWithProvider(new FakeLookupProvider(available: false));
        await AuthenticateAsync(client);

        var status = await (await client.GetAsync("/api/lookup/status"))
            .ReadAsAsync<LookupStatusResponse>();
        Assert.False(status.Available);
    }

    [Fact]
    public async Task Lookup_WhenUnavailable_Returns503()
    {
        var client = CreateClientWithProvider(new FakeLookupProvider(available: false));
        await AuthenticateAsync(client);

        var response = await client.PostAsJsonAsync("/api/lookup", new { term = "hund" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Lookup_ReturnsStructuredResult_ForUsersLanguages()
    {
        var client = CreateClientWithProvider(new FakeLookupProvider(available: true));
        await AuthenticateAsync(client);

        var result = await (await client.PostAsJsonAsync("/api/lookup", new { term = "hund" }))
            .ReadAsAsync<WordLookupResult>();

        Assert.Equal("hund", result.Term);
        // Default settings: known languages = ["en"].
        Assert.Equal(["meaning-en"], result.Meanings["en"]);
        Assert.Equal("translation-en", result.ExampleTranslations["en"]);
    }

    [Fact]
    public async Task Lookup_WhenProviderFails_Returns502()
    {
        var client = CreateClientWithProvider(new FakeLookupProvider(available: true, throws: true));
        await AuthenticateAsync(client);

        var response = await client.PostAsJsonAsync("/api/lookup", new { term = "hund" });
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact]
    public async Task Lookup_WhenProviderCancelsItself_Returns502()
    {
        // Not the caller's cancellation, so it is a provider failure (FR-P4).
        var client = CreateClientWithProvider(new FakeLookupProvider(available: true)
        {
            LookupFailure = new OperationCanceledException(),
        });
        await AuthenticateAsync(client);

        var response = await client.PostAsJsonAsync("/api/lookup", new { term = "hund" });
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact]
    public async Task Lookup_RequiresAuthentication()
    {
        var client = CreateClientWithProvider(new FakeLookupProvider(available: true));
        var response = await client.PostAsJsonAsync("/api/lookup", new { term = "hund" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private record LookupStatusResponse(bool Available, string Provider);
}
