using System.Text.Json;
using AnkiLearner.Core;
using AnkiLearner.Core.Abstractions;
using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Options;

namespace AnkiLearner.Infrastructure.Lookup;

/// <summary>
/// Word lookup via the Claude API with structured outputs (spec FR-P2). The prompt and
/// JSON schema are generalized to any target language and set of known languages;
/// ported from the DanishLearner POC's WordLookupService.
/// </summary>
public class ClaudeLookupProvider : IWordLookupProvider
{
    private readonly AnthropicOptions _options;
    private readonly AnthropicClient? _client;

    public ClaudeLookupProvider(IOptions<AnthropicOptions> options)
    {
        _options = options.Value;
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            _client = new AnthropicClient { ApiKey = _options.ApiKey };
    }

    public string Name => $"Claude ({_options.Model})";

    public bool IsAvailable => _client is not null;

    public async Task<WordLookupResult> LookupAsync(
        string term, string targetLanguage, IReadOnlyList<string> knownLanguages, CancellationToken ct)
    {
        if (_client is null)
            throw new InvalidOperationException("AI lookup is not configured (Anthropic:ApiKey is missing).");

        var response = await _client.Messages.Create(new MessageCreateParams
        {
            Model = _options.Model,
            MaxTokens = _options.MaxTokens,
            System = BuildSystemPrompt(targetLanguage, knownLanguages),
            OutputConfig = new OutputConfig
            {
                Format = new JsonOutputFormat { Schema = BuildSchema(knownLanguages) },
            },
            Messages = [new() { Role = Role.User, Content = term.Trim() }],
        }, cancellationToken: ct);

        var json = response.Content
            .Select(b => b.Value)
            .OfType<TextBlock>()
            .Select(t => t.Text)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException("The lookup provider returned an empty response.");

        return ParseResult(json, knownLanguages);
    }

    /// <summary>Builds the dictionary-style system prompt for the given language pair(s).</summary>
    public static string BuildSystemPrompt(string targetLanguage, IReadOnlyList<string> knownLanguages)
    {
        var target = DisplayName(targetLanguage);
        var known = string.Join(", ", knownLanguages.Select(DisplayName));
        var codes = string.Join(", ", knownLanguages);

        return $"""
            You are a {target} dictionary for a learner who knows: {known}.
            Given a word or phrase in {target}, return:
            - term: the lemma (base/dictionary form) of the input
            - transcription: IPA phonetic transcription of the {target} term, e.g. [ˈhunˀ]
            - part_of_speech: e.g. noun, verb, adjective, adverb, preposition
            - gender: for nouns, the article or gender marker used in {target} (for example "en" or "et" in Danish); otherwise an empty string
            - meanings: for each language code ({codes}), up to 4 distinct meanings in that language, ordered by frequency, each a single word or short phrase (no full sentences)
            - example: one natural, short example sentence in {target} using the term
            - example_translations: that example sentence translated into each language ({codes})
            Be accurate and concise. If the input is not a valid {target} word, return your best guess.
            """;
    }

    /// <summary>JSON schema for structured outputs, with one entry per known language.</summary>
    public static Dictionary<string, JsonElement> BuildSchema(IReadOnlyList<string> knownLanguages)
    {
        var meaningsProperties = knownLanguages.ToDictionary(
            code => code,
            object (_) => new { type = "array", items = new { type = "string" } });
        var translationProperties = knownLanguages.ToDictionary(
            code => code,
            object (_) => new { type = "string" });

        return new Dictionary<string, JsonElement>
        {
            ["type"] = JsonSerializer.SerializeToElement("object"),
            ["properties"] = JsonSerializer.SerializeToElement(new Dictionary<string, object>
            {
                ["term"] = new { type = "string" },
                ["transcription"] = new { type = "string" },
                ["part_of_speech"] = new { type = "string" },
                ["gender"] = new { type = "string" },
                ["meanings"] = new
                {
                    type = "object",
                    properties = meaningsProperties,
                    required = knownLanguages,
                    additionalProperties = false,
                },
                ["example"] = new { type = "string" },
                ["example_translations"] = new
                {
                    type = "object",
                    properties = translationProperties,
                    required = knownLanguages,
                    additionalProperties = false,
                },
            }),
            ["required"] = JsonSerializer.SerializeToElement(new[]
            {
                "term", "transcription", "part_of_speech", "gender",
                "meanings", "example", "example_translations",
            }),
            ["additionalProperties"] = JsonSerializer.SerializeToElement(false),
        };
    }

    /// <summary>Maps the model's JSON to the result, tolerating missing language entries.</summary>
    public static WordLookupResult ParseResult(string json, IReadOnlyList<string> knownLanguages)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var meanings = new Dictionary<string, List<string>>();
        var exampleTranslations = new Dictionary<string, string>();
        foreach (var code in knownLanguages)
        {
            meanings[code] =
                root.TryGetProperty("meanings", out var m) &&
                m.ValueKind == JsonValueKind.Object &&
                m.TryGetProperty(code, out var list) &&
                list.ValueKind == JsonValueKind.Array
                    ? [.. list.EnumerateArray().Select(e => e.GetString() ?? string.Empty).Where(s => s.Length > 0)]
                    : [];
            exampleTranslations[code] =
                root.TryGetProperty("example_translations", out var t) &&
                t.ValueKind == JsonValueKind.Object &&
                t.TryGetProperty(code, out var text)
                    ? text.GetString() ?? string.Empty
                    : string.Empty;
        }

        return new WordLookupResult(
            GetString(root, "term"),
            GetString(root, "transcription"),
            GetString(root, "part_of_speech"),
            GetString(root, "gender"),
            meanings,
            GetString(root, "example"),
            exampleTranslations);
    }

    private static string GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static string DisplayName(string code) =>
        LanguageCatalog.All.FirstOrDefault(l => l.Code == code)?.Name ?? code;
}
