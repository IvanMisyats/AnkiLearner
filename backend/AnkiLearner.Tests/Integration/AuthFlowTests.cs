using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AnkiLearner.Api.Contracts;

namespace AnkiLearner.Tests.Integration;

public class AuthFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static string UniqueEmail() => $"user-{Guid.NewGuid():N}@test.local";

    [Fact]
    public async Task Register_Login_Me_Refresh_Logout_FullFlow()
    {
        var client = factory.CreateClient();
        var email = UniqueEmail();

        // Register → 201 with access token, refresh cookie set
        var registerResponse = await client.PostAsJsonAsync("/api/auth/register",
            new { email, password = "password123" });
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);
        var registered = await registerResponse.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(registered);
        Assert.False(string.IsNullOrEmpty(registered.AccessToken));
        Assert.Equal(email, registered.User.Email);

        // Me with the access token → default settings
        var me = await GetMeAsync(client, registered.AccessToken);
        Assert.Equal(email, me.User.Email);
        Assert.Equal("da", me.Settings.LearningLanguage);
        Assert.Equal(["en"], me.Settings.KnownLanguages);
        Assert.Equal(20, me.Settings.DailyNewLimit);

        // Refresh (cookie travels automatically) → new access token
        var refreshResponse = await client.PostAsync("/api/auth/refresh", null);
        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
        var refreshed = await refreshResponse.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(refreshed);
        Assert.False(string.IsNullOrEmpty(refreshed.AccessToken));

        // Logout revokes the refresh token
        var logoutResponse = await client.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);

        // Refresh after logout → 401 (cookie cleared / token revoked)
        var refreshAfterLogout = await client.PostAsync("/api/auth/refresh", null);
        Assert.Equal(HttpStatusCode.Unauthorized, refreshAfterLogout.StatusCode);
    }

    [Fact]
    public async Task Refresh_ReusingRotatedToken_IsRejected()
    {
        var client = factory.CreateClient();
        var handlerClient = factory.CreateDefaultClient(); // separate cookie jar

        var email = UniqueEmail();
        var register = await client.PostAsJsonAsync("/api/auth/register",
            new { email, password = "password123" });
        var oldCookie = register.Headers.GetValues("Set-Cookie")
            .First(c => c.StartsWith("ankilearner_refresh="));

        // Rotate once via the normal client (gets a new cookie).
        var refresh = await client.PostAsync("/api/auth/refresh", null);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);

        // Replay the ORIGINAL (now rotated-away) cookie manually → rejected.
        var replay = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        replay.Headers.Add("Cookie", oldCookie.Split(';')[0]);
        var replayResponse = await handlerClient.SendAsync(replay);
        Assert.Equal(HttpStatusCode.Unauthorized, replayResponse.StatusCode);
    }

    [Fact]
    public async Task Login_WithWrongPassword_Returns401()
    {
        var client = factory.CreateClient();
        var email = UniqueEmail();
        await client.PostAsJsonAsync("/api/auth/register", new { email, password = "password123" });

        var login = await client.PostAsJsonAsync("/api/auth/login",
            new { email, password = "wrong-password" });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Fact]
    public async Task Me_WithoutToken_Returns401()
    {
        var client = factory.CreateClient();
        var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task Register_WithDuplicateEmail_Returns400()
    {
        var client = factory.CreateClient();
        var email = UniqueEmail();
        await client.PostAsJsonAsync("/api/auth/register", new { email, password = "password123" });

        var second = await client.PostAsJsonAsync("/api/auth/register",
            new { email, password = "password456" });
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [Fact]
    public async Task Register_WithShortPassword_Returns400()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register",
            new { email = UniqueEmail(), password = "short" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Health_Returns200()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<MeResponse> GetMeAsync(HttpClient client, string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<MeResponse>())!;
    }
}
