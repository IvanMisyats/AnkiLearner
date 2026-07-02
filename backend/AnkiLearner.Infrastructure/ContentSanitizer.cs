using AnkiLearner.Core.Abstractions;
using Ganss.Xss;

namespace AnkiLearner.Infrastructure;

/// <summary>
/// HtmlSanitizer-based implementation. The library's default allowlist keeps common
/// formatting (b/i/ul/ol/li/br/hr, tables, spans with safe styles) and strips
/// scripts, event handlers, and dangerous URLs. Thread-safe when configuration is not mutated.
/// </summary>
public class ContentSanitizer : IContentSanitizer
{
    private readonly HtmlSanitizer _sanitizer = new();

    public string Sanitize(string html) => _sanitizer.Sanitize(html);

    public string? SanitizeOrNull(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;
        var clean = _sanitizer.Sanitize(html);
        return string.IsNullOrWhiteSpace(clean) ? null : clean;
    }
}
