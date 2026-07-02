using System.Net.Http.Headers;
using System.Net.Http.Json;
using AnkiLearner.Api.Contracts;

namespace AnkiLearner.Tests.Integration;

public static class TestClientExtensions
{
    /// <summary>Registers a fresh user and returns a client sending its bearer token.</summary>
    public static async Task<HttpClient> CreateAuthenticatedClientAsync(this ApiFactory factory)
    {
        var client = factory.CreateClient();
        var email = $"user-{Guid.NewGuid():N}@test.local";
        var response = await client.PostAsJsonAsync("/api/auth/register",
            new { email, password = "password123" });
        response.EnsureSuccessStatusCode();
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", auth!.AccessToken);
        return client;
    }

    public static async Task<T> ReadAsAsync<T>(this HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
}
