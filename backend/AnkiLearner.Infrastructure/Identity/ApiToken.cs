namespace AnkiLearner.Infrastructure.Identity;

/// <summary>
/// A long-lived personal access token for non-interactive clients (scripts, CLI tools) that
/// cannot hold a browser session. Only the SHA-256 hash is stored; the raw value is shown once
/// at creation and never again. Carries the same rights as the owning user.
/// </summary>
public class ApiToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    /// <summary>Human label so tokens can be told apart when revoking, e.g. "claude-skill".</summary>
    public string Name { get; set; } = string.Empty;

    public string TokenHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    /// <summary>Refreshed at most hourly — see ApiTokenService.ValidateAsync.</summary>
    public DateTime? LastUsedAt { get; set; }

    /// <summary>Null means the token never expires.</summary>
    public DateTime? ExpiresAt { get; set; }

    public DateTime? RevokedAt { get; set; }
}
