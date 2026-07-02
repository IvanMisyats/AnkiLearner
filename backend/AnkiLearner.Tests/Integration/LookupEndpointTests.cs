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

    public Task<WordLookupResult> LookupAsync(
        string term, string targetLanguage, IReadOnlyList<string> knownLanguages, CancellationToken ct)
    {
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
    public async Task Lookup_RequiresAuthentication()
    {
        var client = CreateClientWithProvider(new FakeLookupProvider(available: true));
        var response = await client.PostAsJsonAsync("/api/lookup", new { term = "hund" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private record LookupStatusResponse(bool Available, string Provider);
}
