using System.Text.RegularExpressions;

namespace AnkiLearner.Core;

/// <summary>
/// Produces the plain-text form of a term used for duplicate detection
/// (spec FR-W7/FR-I4): HTML stripped, whitespace collapsed, trimmed, lower-cased.
/// </summary>
public static partial class TermNormalizer
{
    /// <summary>Leading particles that matching ignores, per learning language ("en aftale" ≈
    /// "aftale"). Add an entry here to make another language article-insensitive; languages
    /// without one match exactly.</summary>
    private static readonly Dictionary<string, string[]> Particles =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["da"] = ["en", "et", "at"],
        };

    /// <summary>True when the language has a particle table, i.e. its citation forms carry an
    /// article / infinitive marker ("en aftale", "at spise").</summary>
    public static bool HasParticles(string languageCode) => Particles.ContainsKey(languageCode);

    public static string Normalize(string htmlTerm) => PlainText(htmlTerm).ToLowerInvariant();

    /// <summary>The term as plain text, case kept: HTML stripped and decoded, whitespace
    /// collapsed, trimmed ("&lt;b&gt;en hund&lt;/b&gt;" → "en hund").</summary>
    public static string PlainText(string htmlTerm)
    {
        var text = TagsRegex().Replace(htmlTerm, " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        return WhitespaceRegex().Replace(text, " ").Trim();
    }

    /// <summary>The normalized term without one leading particle ("en aftale" → "aftale").
    /// A particle is stripped only when a space and something more follow it, so "enkel" and a
    /// bare "en" stay as they are.</summary>
    public static string MatchKey(string htmlTerm, string languageCode)
    {
        var normalized = Normalize(htmlTerm);
        if (!Particles.TryGetValue(languageCode, out var particles)) return normalized;

        foreach (var particle in particles)
        {
            var prefix = particle + " ";
            // Normalize trims and collapses whitespace, so anything after the prefix is a word.
            if (normalized.StartsWith(prefix, StringComparison.Ordinal) && normalized.Length > prefix.Length)
                return normalized[prefix.Length..];
        }
        return normalized;
    }

    /// <summary>Every stored <c>TermNormalized</c> value that counts as the same term: the match
    /// key bare and with each particle in front. Empty input → no candidates.</summary>
    public static IReadOnlyList<string> MatchCandidates(string htmlTerm, string languageCode)
    {
        var normalized = Normalize(htmlTerm);
        if (normalized.Length == 0) return [];
        if (!Particles.TryGetValue(languageCode, out var particles)) return [normalized];

        var key = MatchKey(htmlTerm, languageCode);
        return particles.Select(p => p + " " + key).Prepend(key).Distinct().ToList();
    }

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex TagsRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
