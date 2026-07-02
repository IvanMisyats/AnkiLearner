namespace AnkiLearner.Infrastructure.Identity;

/// <summary>
/// Server-side record of an issued refresh token. Only the SHA-256 hash is stored;
/// the raw token lives in an httpOnly cookie on the client. Rotated on every refresh.
/// </summary>
public class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}
