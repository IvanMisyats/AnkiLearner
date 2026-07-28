using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using AnkiLearner.Infrastructure.Data;
using AnkiLearner.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace AnkiLearner.Api.Auth;

/// <summary>Issues, validates, and revokes API tokens (stored as SHA-256 hashes).</summary>
public class ApiTokenService(AppDbContext db)
{
    /// <summary>Marks a bearer value as an API token rather than a JWT.</summary>
    public const string Prefix = "ankl_";

    /// <summary>How stale LastUsedAt is allowed to get — see ValidateAsync.</summary>
    private static readonly TimeSpan LastUsedResolution = TimeSpan.FromHours(1);

    /// <summary>Returns the raw token; it is shown to the caller once and never stored.</summary>
    public async Task<(ApiToken Token, string Raw)> IssueAsync(
        Guid userId, string name, int? expiresInDays, CancellationToken ct)
    {
        // Base64url, not base64: the value gets pasted into shells, headers and env vars,
        // where '+' and '/' need escaping.
        var raw = Prefix + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        var token = new ApiToken
        {
            UserId = userId,
            Name = name,
            TokenHash = Hash(raw),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = expiresInDays is > 0 ? DateTime.UtcNow.AddDays(expiresInDays.Value) : null,
        };
        db.ApiTokens.Add(token);
        await db.SaveChangesAsync(ct);
        return (token, raw);
    }

    /// <summary>Returns the owning user id, or null when the token is unknown/revoked/expired.</summary>
    public async Task<Guid?> ValidateAsync(string raw, CancellationToken ct)
    {
        if (!raw.StartsWith(Prefix, StringComparison.Ordinal)) return null;

        var hash = Hash(raw);
        var token = await db.ApiTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        var now = DateTime.UtcNow;
        // ExpiresAt == null means "never expires" — spelled out rather than relying on
        // lifted-operator semantics, because misreading it would be a security bug.
        if (token is null || token.RevokedAt is not null ||
            (token.ExpiresAt is not null && token.ExpiresAt <= now))
            return null;

        // LastUsedAt only has to answer "is this token still in use?", so it is written at most
        // once an hour rather than on every request — otherwise every GET would cause a write.
        if (token.LastUsedAt is null || now - token.LastUsedAt.Value > LastUsedResolution)
        {
            token.LastUsedAt = now;
            await db.SaveChangesAsync(ct);
        }

        return token.UserId;
    }

    public async Task<List<ApiToken>> ListAsync(Guid userId, CancellationToken ct) =>
        await db.ApiTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .OrderByDescending(t => t.CreatedAt)
            .AsNoTracking()
            .ToListAsync(ct);

    /// <summary>False when the token does not exist or belongs to someone else.</summary>
    public async Task<bool> RevokeAsync(Guid userId, Guid id, CancellationToken ct)
    {
        var token = await db.ApiTokens.FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId, ct);
        if (token is null) return false;

        if (token.RevokedAt is null)
        {
            token.RevokedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        return true;
    }

    // Same scheme as refresh tokens (TokenService.HashRefreshToken): the raw value is
    // high-entropy, so a plain hash lookup is enough — no salt or KDF needed.
    private static string Hash(string raw) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
}
