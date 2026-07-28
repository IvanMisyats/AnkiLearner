using AnkiLearner.Api.Auth;
using AnkiLearner.Api.Contracts;
using AnkiLearner.Core.Abstractions;
using AnkiLearner.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AnkiLearner.Api.Controllers;

/// <summary>
/// Manages personal access tokens for non-interactive clients.
///
/// Pinned to the JWT scheme on purpose: an API token cannot mint, list, or revoke tokens, so a
/// leaked one can never extend or hide itself — only a password login can manage them.
/// </summary>
[ApiController]
[Route("api/tokens")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[EnableRateLimiting("auth")]
public class ApiTokensController(ApiTokenService tokens, ICurrentUser currentUser) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<ApiTokenDto>>> List(CancellationToken ct)
    {
        var items = await tokens.ListAsync(currentUser.UserId, ct);
        return items.Select(ToDto).ToList();
    }

    [HttpPost]
    public async Task<ActionResult<CreatedApiTokenResponse>> Create(
        CreateApiTokenRequest request, CancellationToken ct)
    {
        var (token, raw) = await tokens.IssueAsync(
            currentUser.UserId, request.Name.Trim(), request.ExpiresInDays, ct);
        // Location points at the collection: a token is never individually retrievable.
        return CreatedAtAction(nameof(List), new CreatedApiTokenResponse(ToDto(token), raw));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken ct) =>
        await tokens.RevokeAsync(currentUser.UserId, id, ct) ? NoContent() : NotFound();

    private static ApiTokenDto ToDto(ApiToken t) =>
        new(t.Id, t.Name, t.CreatedAt, t.LastUsedAt, t.ExpiresAt);
}
