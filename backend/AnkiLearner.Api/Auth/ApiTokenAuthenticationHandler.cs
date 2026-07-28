using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace AnkiLearner.Api.Auth;

/// <summary>
/// Authenticates "Authorization: Bearer ankl_..." requests. The principal carries the same
/// NameIdentifier claim the JWT handler produces, so <see cref="CurrentUser"/> — and therefore
/// every [Authorize] controller — works unchanged whichever credential was used.
/// </summary>
public class ApiTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ApiTokenService tokens) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    private const string BearerPrefix = "Bearer ";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // OrdinalIgnoreCase to match the scheme selector in Program.cs — the auth scheme name is
        // case-insensitive per RFC 7235. The token itself is still matched exactly, by hash.
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith(BearerPrefix + ApiTokenService.Prefix, StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        var raw = header[BearerPrefix.Length..].Trim();
        var userId = await tokens.ValidateAsync(raw, Context.RequestAborted);
        if (userId is null)
            return AuthenticateResult.Fail("Invalid, revoked, or expired API token.");

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString())],
            Scheme.Name);
        return AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }
}
