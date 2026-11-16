namespace AnkiLearner.Infrastructure.Lookup;

public class AnthropicOptions
{
    /// <summary>Server-level API key (spec decision #5). Empty ⇒ AI lookup is disabled.</summary>
    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = "claude-haiku-4-5";

    /// <summary>Output cap per call. Only a ceiling: a 5-card translation stays well below it.</summary>
    public int MaxTokens { get; set; } = 4096;

    /// <summary>Total wall-clock limit per AI call, the SDK's retries included.</summary>
    public int TimeoutSeconds { get; set; } = 30;
}
