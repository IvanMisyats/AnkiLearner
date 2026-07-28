using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AnkiLearner.Api.Contracts;
using AnkiLearner.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AnkiLearner.Tests.Integration;

public class ApiTokenTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task IssuedToken_ActsAsTheOwningUser()
    {
        var owner = await factory.CreateAuthenticatedClientAsync();
        var tokenClient = await IssueTokenClientAsync(owner);

        // The two endpoints the non-interactive clients actually need.
        var me = await (await tokenClient.GetAsync("/api/auth/me")).ReadAsAsync<MeResponse>();
        Assert.Equal("da", me.Settings.LearningLanguage);

        var create = await tokenClient.PostAsJsonAsync("/api/words", new
        {
            term = "hyggelig",
            translations = new[] { new { languageCode = "en", text = "cosy" } },
            tags = new[] { "claude" },
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        // Same user: the word created with the token is visible to the password session.
        var words = await (await owner.GetAsync("/api/words")).ReadAsAsync<PagedResponse<WordDto>>();
        Assert.Contains(words.Items, w => w.Term == "hyggelig");
    }

    [Fact]
    public async Task RawTokenIsReturnedOnce_AndNeverListed()
    {
        var owner = await factory.CreateAuthenticatedClientAsync();
        var created = await (await owner.PostAsJsonAsync("/api/tokens", new { name = "cli" }))
            .ReadAsAsync<CreatedApiTokenResponse>();

        Assert.StartsWith("ankl_", created.Value);
        Assert.Equal("cli", created.Token.Name);
        Assert.Null(created.Token.ExpiresAt);

        var listed = await (await owner.GetAsync("/api/tokens")).ReadAsAsync<List<ApiTokenDto>>();
        var match = Assert.Single(listed, t => t.Id == created.Token.Id);
        Assert.Equal("cli", match.Name);
        // The DTO has no field that could carry the secret.
        Assert.DoesNotContain(created.Value, await (await owner.GetAsync("/api/tokens")).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task RevokedToken_IsRejected()
    {
        var owner = await factory.CreateAuthenticatedClientAsync();
        var created = await (await owner.PostAsJsonAsync("/api/tokens", new { name = "doomed" }))
            .ReadAsAsync<CreatedApiTokenResponse>();
        var tokenClient = ClientWith(created.Value);

        Assert.Equal(HttpStatusCode.OK, (await tokenClient.GetAsync("/api/auth/me")).StatusCode);

        var revoke = await owner.DeleteAsync($"/api/tokens/{created.Token.Id}");
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await tokenClient.GetAsync("/api/auth/me")).StatusCode);
        Assert.DoesNotContain(
            await (await owner.GetAsync("/api/tokens")).ReadAsAsync<List<ApiTokenDto>>(),
            t => t.Id == created.Token.Id);
    }

    [Fact]
    public async Task ExpiredToken_IsRejected()
    {
        var owner = await factory.CreateAuthenticatedClientAsync();
        var created = await (await owner.PostAsJsonAsync("/api/tokens", new { name = "short-lived", expiresInDays = 1 }))
            .ReadAsAsync<CreatedApiTokenResponse>();
        var tokenClient = ClientWith(created.Value);
        Assert.Equal(HttpStatusCode.OK, (await tokenClient.GetAsync("/api/auth/me")).StatusCode);

        // No way to move the clock from outside, so age the row directly.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.ApiTokens.Where(t => t.Id == created.Token.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.ExpiresAt, DateTime.UtcNow.AddMinutes(-1)));
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await tokenClient.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task ApiToken_CannotManageTokens()
    {
        var owner = await factory.CreateAuthenticatedClientAsync();
        var tokenClient = await IssueTokenClientAsync(owner);

        // A leaked token must not be able to mint a replacement, enumerate, or revoke.
        Assert.Equal(HttpStatusCode.Unauthorized, (await tokenClient.GetAsync("/api/tokens")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await tokenClient.PostAsJsonAsync("/api/tokens", new { name = "escalate" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await tokenClient.DeleteAsync($"/api/tokens/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task UnknownOrMalformedToken_IsRejected()
    {
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await ClientWith("ankl_not-a-real-token").GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await ClientWith("ankl_").GetAsync("/api/auth/me")).StatusCode);
        // Not prefixed: routed to the JWT handler, which also rejects it.
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await ClientWith("not-a-jwt").GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task TokensAreScopedToTheirOwner()
    {
        var alice = await factory.CreateAuthenticatedClientAsync();
        var bob = await factory.CreateAuthenticatedClientAsync();
        var created = await (await alice.PostAsJsonAsync("/api/tokens", new { name = "alice-cli" }))
            .ReadAsAsync<CreatedApiTokenResponse>();

        Assert.Empty(await (await bob.GetAsync("/api/tokens")).ReadAsAsync<List<ApiTokenDto>>());
        Assert.Equal(HttpStatusCode.NotFound, (await bob.DeleteAsync($"/api/tokens/{created.Token.Id}")).StatusCode);
    }

    [Fact]
    public async Task LastUsedAt_IsRecorded()
    {
        var owner = await factory.CreateAuthenticatedClientAsync();
        var created = await (await owner.PostAsJsonAsync("/api/tokens", new { name = "tracked" }))
            .ReadAsAsync<CreatedApiTokenResponse>();
        Assert.Null(created.Token.LastUsedAt);

        await ClientWith(created.Value).GetAsync("/api/auth/me");

        var listed = await (await owner.GetAsync("/api/tokens")).ReadAsAsync<List<ApiTokenDto>>();
        Assert.NotNull(listed.Single(t => t.Id == created.Token.Id).LastUsedAt);
    }

    [Fact]
    public async Task LowercaseBearerScheme_IsAccepted()
    {
        var owner = await factory.CreateAuthenticatedClientAsync();
        var created = await (await owner.PostAsJsonAsync("/api/tokens", new { name = "lowercase" }))
            .ReadAsAsync<CreatedApiTokenResponse>();

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"bearer {created.Value}");

        // RFC 7235 makes the scheme name case-insensitive; the token must not fall through to
        // the JWT handler, which would reject it (and hand it to a parser it doesn't belong in).
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    private HttpClient ClientWith(string rawToken)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", rawToken);
        return client;
    }

    private async Task<HttpClient> IssueTokenClientAsync(HttpClient owner)
    {
        var created = await (await owner.PostAsJsonAsync("/api/tokens", new { name = "claude-skill" }))
            .ReadAsAsync<CreatedApiTokenResponse>();
        return ClientWith(created.Value);
    }
}
