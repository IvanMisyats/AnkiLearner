using AnkiLearner.Infrastructure.Data;
using AnkiLearner.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace AnkiLearner.Api.Auth;

/// <summary>Issues, rotates, and revokes refresh tokens (stored as SHA-256 hashes).</summary>
public class RefreshTokenService(AppDbContext db, TokenService tokens)
{
    public async Task<(string Raw, DateTime Expires)> IssueAsync(Guid userId, CancellationToken ct)
    {
        var (raw, hash) = tokens.CreateRefreshToken();
        var expires = tokens.RefreshTokenExpiry();
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = userId,
            TokenHash = hash,
            ExpiresAt = expires,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);
        return (raw, expires);
    }

    /// <summary>Validates a raw token and rotates it. Returns null when invalid/expired/revoked.</summary>
    public async Task<(Guid UserId, string NewRaw, DateTime Expires)?> RotateAsync(string raw, CancellationToken ct)
    {
        var hash = TokenService.HashRefreshToken(raw);
        var token = await db.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (token is null || token.RevokedAt is not null || token.ExpiresAt <= DateTime.UtcNow)
            return null;

        token.RevokedAt = DateTime.UtcNow;
        var (newRaw, newHash) = tokens.CreateRefreshToken();
        var expires = tokens.RefreshTokenExpiry();
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = token.UserId,
            TokenHash = newHash,
            ExpiresAt = expires,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);
        return (token.UserId, newRaw, expires);
    }

    public async Task RevokeAsync(string raw, CancellationToken ct)
    {
        var hash = TokenService.HashRefreshToken(raw);
        var token = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (token is not null && token.RevokedAt is null)
        {
            token.RevokedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
    }
}
