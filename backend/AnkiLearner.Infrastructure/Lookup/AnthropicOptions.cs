namespace AnkiLearner.Infrastructure.Lookup;

public class AnthropicOptions
{
    /// <summary>Server-level API key (spec decision #5). Empty ⇒ AI lookup is disabled.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Any Claude model id. Changed in config, not code, as new Haiku models ship.</summary>
    public string Model { get; set; } = "claude-haiku-5-5";

    /// <summary>
    /// Thinking depth: low, medium, high, xhigh or max. Lower is faster. "default" (or empty) sends
    /// none, so the model uses its own default — required for models without effort support,
    /// such as claude-haiku-4-5.
    /// </summary>
    public string Effort { get; set; } = "low";

    /// <summary>Output cap per call, thinking included. Only a ceiling: a 5-card translation stays
    /// well below it.</summary>
    public int MaxTokens { get; set; } = 4096;

    /// <summary>Total wall-clock limit per AI call, the SDK's retries included.</summary>
    public int TimeoutSeconds { get; set; } = 30;
}
