using System.Text.RegularExpressions;

namespace AnkiLearner.Core;

/// <summary>
/// Produces the plain-text form of a term used for duplicate detection
/// (spec FR-W7/FR-I4): HTML stripped, whitespace collapsed, trimmed, lower-cased.
/// </summary>
public static partial class TermNormalizer
{
    public static string Normalize(string htmlTerm)
    {
        var text = TagsRegex().Replace(htmlTerm, " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        text = WhitespaceRegex().Replace(text, " ").Trim();
        return text.ToLowerInvariant();
    }

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex TagsRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
