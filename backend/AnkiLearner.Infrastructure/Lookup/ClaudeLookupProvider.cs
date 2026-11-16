using System.Text.Json;
using AnkiLearner.Core;
using AnkiLearner.Core.Abstractions;
using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Options;

namespace AnkiLearner.Infrastructure.Lookup;

/// <summary>
/// Word lookup and Translate via the Claude API with structured outputs (spec FR-P2, §3.8). The
/// prompts and JSON schemas are generalized to any target language and set of known languages;
/// the lookup was ported from the DanishLearner POC's WordLookupService. Both share one card
/// schema and one citation-form rule, so both ways of adding a word save it alike.
/// </summary>
public class ClaudeLookupProvider : IWordLookupProvider
{
    private readonly AnthropicOptions _options;
    private readonly AnthropicClient? _client;

    public ClaudeLookupProvider(IOptions<AnthropicOptions> options)
    {
        _options = options.Value;
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            // One retry; both attempts stay inside the TimeoutSeconds cap (see CreateAsync).
            _client = new AnthropicClient { ApiKey = _options.ApiKey, MaxRetries = 1 };
    }

    public string Name => $"Claude ({_options.Model})";

    public bool IsAvailable => _client is not null;

    public async Task<WordLookupResult> LookupAsync(
        string term, string targetLanguage, IReadOnlyList<string> knownLanguages, CancellationToken ct)
    {
        var json = await CreateAsync(
            BuildSystemPrompt(targetLanguage, knownLanguages),
            BuildSchema(knownLanguages),
            term.Trim(),
            ct);
        return ParseResult(json, knownLanguages);
    }

    public async Task<TranslateResult> TranslateAsync(
        string text, string? sourceLanguage, string targetLanguage, IReadOnlyList<string> knownLanguages,
        CancellationToken ct)
    {
        var json = await CreateAsync(
            BuildTranslateSystemPrompt(targetLanguage, knownLanguages),
            BuildTranslateSchema(targetLanguage, knownLanguages),
            BuildTranslateUserMessage(text, sourceLanguage),
            ct);
        return ParseTranslateResult(json, text, targetLanguage, knownLanguages);
    }

    /// <summary>One structured-output call; returns the JSON text. The exceptions this class throws
    /// never contain the user's text or the model's output; an SDK exception may carry the API's
    /// error body.</summary>
    private async Task<string> CreateAsync(
        string system, Dictionary<string, JsonElement> schema, string userMessage, CancellationToken ct)
    {
        if (_client is null)
            throw new InvalidOperationException("AI lookup is not configured (Anthropic:ApiKey is missing).");

        var response = await WithTimeoutAsync(
            token => _client.Messages.Create(new MessageCreateParams
            {
                Model = _options.Model,
                MaxTokens = _options.MaxTokens,
                System = system,
                OutputConfig = new OutputConfig
                {
                    Format = new JsonOutputFormat { Schema = schema },
                },
                Messages = [new() { Role = Role.User, Content = userMessage }],
            }, cancellationToken: token),
            TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds)),
            ct);

        EnsureCompleted(response.StopReason?.Raw(), response.Usage.OutputTokens);

        var json = response.Content
            .Select(b => b.Value)
            .OfType<TextBlock>()
            .Select(t => t.Text)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException("The AI provider returned an empty response.");
        return json;
    }

    /// <summary>Runs <paramref name="call"/> under a total time limit (retries included). Running
    /// out of time is a provider failure (<see cref="TimeoutException"/>); a cancellation of the
    /// caller's own <paramref name="ct"/> still surfaces as <see cref="OperationCanceledException"/>.</summary>
    public static async Task<T> WithTimeoutAsync<T>(
        Func<CancellationToken, Task<T>> call, TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            return await call(cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"The AI provider did not answer within {timeout.TotalSeconds:0} s.");
        }
    }

    /// <summary>A truncated or refused answer is not usable JSON: fail it explicitly (→ 502).</summary>
    public static void EnsureCompleted(string? stopReason, long outputTokens)
    {
        if (stopReason is "max_tokens" or "refusal")
            throw new InvalidOperationException(
                $"The AI provider stopped early (stop reason: {stopReason}, {outputTokens} output tokens).");
    }

    // ---------- Prompts ----------

    /// <summary>The headword rule shared by lookup and Translate (spec FR-W2, FR-T3). Languages
    /// with a particle table in <see cref="TermNormalizer"/> (Danish) save nouns with their article
    /// and verbs with the infinitive marker, which duplicate matching then ignores; every other
    /// language keeps the plain lemma.</summary>
    public static string CitationFormRule(string targetLanguage)
    {
        if (!TermNormalizer.HasParticles(targetLanguage))
            return "term: the lemma (base/dictionary form)";

        var examples = targetLanguage == "da" ? """ (Danish: "en aftale", "et minde", "at spise")""" : "";
        return $"term: the dictionary citation form — nouns with their indefinite article and verbs with the infinitive marker{examples}";
    }

    /// <summary>The card fields both prompts describe the same way.</summary>
    private static string HeadwordFields(string targetLanguage)
    {
        var target = LanguageCatalog.DisplayName(targetLanguage);
        var ipaOf = TermNormalizer.HasParticles(targetLanguage)
            ? $"the {target} word itself (no article or infinitive marker)"
            : $"the {target} term";
        // Danish examples only where they are true.
        var danish = targetLanguage == "da";
        var ipaExample = danish ? ", e.g. [ˈhunˀ]" : ", in square brackets";
        var genderExample = danish ? """ (for example "en" or "et")""" : "";

        return $"""
            - {CitationFormRule(targetLanguage)}
            - transcription: IPA phonetic transcription of {ipaOf}{ipaExample}
            - part_of_speech: e.g. noun, verb, adjective, adverb, preposition, expression
            - gender: for nouns, the article or gender marker used in {target}{genderExample}; otherwise an empty string
            """;
    }

    /// <summary>Builds the dictionary-style system prompt for the given language pair(s).</summary>
    public static string BuildSystemPrompt(string targetLanguage, IReadOnlyList<string> knownLanguages)
    {
        var target = LanguageCatalog.DisplayName(targetLanguage);
        var known = string.Join(", ", knownLanguages.Select(LanguageCatalog.DisplayName));
        var codes = string.Join(", ", knownLanguages);

        return $"""
            You are a {target} dictionary for a learner who knows: {known}.
            Given a word or phrase in {target}, return:
            {HeadwordFields(targetLanguage)}
            - meanings: for each language code ({codes}), up to 4 distinct meanings in that language, ordered by frequency, each a single word or short phrase (no full sentences)
            - example: one natural, short example sentence in {target} using the term
            - example_translations: that example sentence translated into each language ({codes})
            Be accurate and concise. If the input is not a valid {target} word, return your best guess.
            """;
    }

    /// <summary>System prompt for Translate. Kept compact: on Haiku, output length is latency.</summary>
    public static string BuildTranslateSystemPrompt(string targetLanguage, IReadOnlyList<string> knownLanguages)
    {
        var target = LanguageCatalog.DisplayName(targetLanguage);
        var known = string.Join(", ", knownLanguages.Select(LanguageCatalog.DisplayName));
        var knownCodes = string.Join(", ", knownLanguages);
        var allCodes = string.Join(", ", knownLanguages.Prepend(targetLanguage));
        var codeList = string.Join(", ", knownLanguages.Prepend(targetLanguage)
            .Select(c => $"{c} ({LanguageCatalog.DisplayName(c)})"));
        var firstKnown = LanguageCatalog.DisplayName(knownLanguages.FirstOrDefault() ?? "en");
        var other = TranslateShaper.OtherLanguage;
        // Danish examples only where they are true.
        var danish = targetLanguage == "da";
        var ambiguousExample = danish ? """ (like "gift" or "barn")""" : "";
        var homographExample = danish ? """ (like "gift" and "en gift")""" : "";

        return $"""
            You are a {target} dictionary and translator for a learner who knows: {known}.
            The user sends a word, phrase or sentence. Return:
            - source_language: the input's language, one of {codeList}; "{other}" if it is none of them — then every input_translations value is "" and cards is empty. A word that exists in several of these languages{ambiguousExample}: prefer {target}. If the message states the source language, use it.
            - input_kind: "word"; "phrase" (a multi-word expression, not a full sentence); or "sentence"
            - input_translations: the whole input in each language ({allCodes}); "" for the source language
            - cards: {target} entries worth learning, usually 1-3, at most {TranslateShaper.MaxCards}. One card per citation form, listing all its senses in meanings; separate cards only when the citation forms differ{homographExample}.
              - a {target} word: its entry
              - a word or phrase in another language: its {target} equivalents, most common first, each note saying how it differs
              - a {target} phrase or any sentence: the words and expressions worth learning from the {target} text (the input or its {target} translation); skip trivial function words; keep idioms and fixed expressions whole
            Each card:
            {HeadwordFields(targetLanguage)}
            - meanings: for each language code ({knownCodes}), up to 3 short meanings in that language, most common first
            - example: one short, natural {target} sentence using the term; "" when input_kind is "sentence"
            - example_translations: the example in each language ({knownCodes}); "" when example is ""
            - note: one short sentence in {firstKnown} on usage or how it differs from the alternatives; "" if nothing useful
            Be accurate and concise.
            """;
    }

    /// <summary>The user turn. A chosen source language goes here rather than into the schema,
    /// so every request of a user shares one schema (each new schema has a compile cost).</summary>
    public static string BuildTranslateUserMessage(string text, string? sourceLanguage) =>
        string.IsNullOrEmpty(sourceLanguage)
            ? text
            : $"Source language: {sourceLanguage} ({LanguageCatalog.DisplayName(sourceLanguage)}), set by the user — do not detect.\n{text}";

    // ---------- Schemas ----------

    /// <summary>JSON schema for the lookup: one card, with one entry per known language.</summary>
    public static Dictionary<string, JsonElement> BuildSchema(IReadOnlyList<string> knownLanguages) =>
        ToSchema(CardSchema(knownLanguages, withNote: false));

    /// <summary>JSON schema for Translate. Depends only on the user's languages, so it is
    /// identical for all of a user's requests.</summary>
    public static Dictionary<string, JsonElement> BuildTranslateSchema(
        string targetLanguage, IReadOnlyList<string> knownLanguages)
    {
        var languages = knownLanguages.Prepend(targetLanguage).Distinct().ToList();
        return ToSchema(ObjectSchema(new Dictionary<string, object>
        {
            ["source_language"] = new { type = "string", @enum = languages.Append(TranslateShaper.OtherLanguage) },
            ["input_kind"] = new { type = "string", @enum = new[] { "word", "phrase", "sentence" } },
            ["input_translations"] = ObjectSchema(languages.ToDictionary(c => c, object (_) => StringSchema)),
            ["cards"] = new { type = "array", items = CardSchema(knownLanguages, withNote: true) },
        }));
    }

    private static readonly object StringSchema = new { type = "string" };

    private static Dictionary<string, object> CardSchema(IReadOnlyList<string> knownLanguages, bool withNote)
    {
        var properties = new Dictionary<string, object>
        {
            ["term"] = StringSchema,
            ["transcription"] = StringSchema,
            ["part_of_speech"] = StringSchema,
            ["gender"] = StringSchema,
            ["meanings"] = ObjectSchema(knownLanguages.ToDictionary(
                c => c, object (_) => new { type = "array", items = StringSchema })),
            ["example"] = StringSchema,
            ["example_translations"] = ObjectSchema(knownLanguages.ToDictionary(c => c, _ => StringSchema)),
        };
        if (withNote) properties["note"] = StringSchema;
        return ObjectSchema(properties);
    }

    /// <summary>Structured outputs need every object closed and every property required.</summary>
    private static Dictionary<string, object> ObjectSchema(Dictionary<string, object> properties) => new()
    {
        ["type"] = "object",
        ["properties"] = properties,
        ["required"] = properties.Keys.ToArray(),
        ["additionalProperties"] = false,
    };

    private static Dictionary<string, JsonElement> ToSchema(Dictionary<string, object> schema) =>
        JsonSerializer.SerializeToElement(schema).EnumerateObject().ToDictionary(p => p.Name, p => p.Value);

    // ---------- Parsing (tolerant: the model's output is untrusted) ----------

    /// <summary>Maps the model's JSON to the result, tolerating missing language entries.</summary>
    public static WordLookupResult ParseResult(string json, IReadOnlyList<string> knownLanguages)
    {
        using var doc = ParseJson(json);
        var card = ParseCard(RootObject(doc), knownLanguages);
        return new WordLookupResult(
            card.Term, card.Transcription, card.PartOfSpeech, card.Gender,
            card.Meanings, card.Example, card.ExampleTranslations);
    }

    /// <summary>Maps the model's Translate JSON. An unknown or missing source becomes the target
    /// language; an unknown kind becomes Word for <paramref name="text"/> without whitespace,
    /// otherwise Phrase.</summary>
    public static TranslateResult ParseTranslateResult(
        string json, string text, string targetLanguage, IReadOnlyList<string> knownLanguages)
    {
        using var doc = ParseJson(json);
        var root = RootObject(doc);

        var source = GetString(root, "source_language");
        if (source != TranslateShaper.OtherLanguage && source != targetLanguage && !knownLanguages.Contains(source))
            source = targetLanguage;

        var kind = GetString(root, "input_kind") switch
        {
            "word" => InputKind.Word,
            "phrase" => InputKind.Phrase,
            "sentence" => InputKind.Sentence,
            _ => text.Any(char.IsWhiteSpace) ? InputKind.Phrase : InputKind.Word,
        };

        var inputTranslations = Property(root, "input_translations");
        var cards = Property(root, "cards") is { ValueKind: JsonValueKind.Array } list
            ? list.EnumerateArray()
                .Where(c => c.ValueKind == JsonValueKind.Object)
                .Select(c => ParseCard(c, knownLanguages))
                .ToList()
            : [];

        return new TranslateResult(
            source,
            kind,
            knownLanguages.Prepend(targetLanguage).Distinct()
                .ToDictionary(code => code, code => GetString(inputTranslations, code)),
            cards);
    }

    private static TranslateCard ParseCard(JsonElement card, IReadOnlyList<string> knownLanguages)
    {
        var meaningsElement = Property(card, "meanings");
        var translationsElement = Property(card, "example_translations");

        return new TranslateCard(
            GetString(card, "term"),
            GetString(card, "transcription"),
            GetString(card, "part_of_speech"),
            GetString(card, "gender"),
            knownLanguages.ToDictionary(
                code => code,
                code => Property(meaningsElement, code) is { ValueKind: JsonValueKind.Array } list
                    ? list.EnumerateArray()
                        .Where(e => e.ValueKind == JsonValueKind.String)
                        .Select(e => e.GetString() ?? string.Empty)
                        .Where(s => s.Length > 0)
                        .ToList()
                    : []),
            GetString(card, "example"),
            knownLanguages.ToDictionary(code => code, code => GetString(translationsElement, code)),
            GetString(card, "note"));
    }

    /// <summary>A JsonException message can quote the model's output, which must never reach the
    /// logs: replace it, deliberately without the inner exception.</summary>
    private static JsonDocument ParseJson(string json)
    {
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("The provider returned malformed JSON.");
        }
    }

    private static JsonElement RootObject(JsonDocument doc) =>
        doc.RootElement.ValueKind == JsonValueKind.Object
            ? doc.RootElement
            : throw new InvalidOperationException("The AI provider returned JSON that is not an object.");

    /// <summary>The named property, or an Undefined element when absent or not an object.</summary>
    private static JsonElement Property(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : default;

    private static string GetString(JsonElement element, string name) =>
        Property(element, name) is { ValueKind: JsonValueKind.String } value
            ? value.GetString() ?? string.Empty
            : string.Empty;
}
