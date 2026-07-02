namespace AnkiLearner.Core.Abstractions;

/// <summary>Sanitizes user-supplied HTML before it is persisted (spec FR-D7).</summary>
public interface IContentSanitizer
{
    string Sanitize(string html);

    /// <summary>Sanitizes a nullable field; empty results become null.</summary>
    string? SanitizeOrNull(string? html);
}
