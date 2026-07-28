namespace AnkiLearner.Api.Auth;

public static class AuthSchemes
{
    /// <summary>
    /// The default scheme: a policy scheme that inspects the bearer value and forwards to
    /// <see cref="ApiToken"/> or JWT. Endpoints use plain [Authorize] and accept either.
    /// </summary>
    public const string Default = "SmartBearer";

    /// <summary>Long-lived personal access tokens for non-interactive clients.</summary>
    public const string ApiToken = "ApiToken";
}
