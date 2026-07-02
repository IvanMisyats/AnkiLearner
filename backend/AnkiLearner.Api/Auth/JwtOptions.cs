namespace AnkiLearner.Api.Auth;

public class JwtOptions
{
    public string Issuer { get; set; } = "AnkiLearner";
    public string Audience { get; set; } = "AnkiLearner";
    /// <summary>HMAC-SHA256 key, at least 32 characters. No default: each instance must set its own.</summary>
    public string SigningKey { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 30;
}
