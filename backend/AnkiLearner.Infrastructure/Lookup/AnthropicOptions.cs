namespace AnkiLearner.Infrastructure.Lookup;

public class AnthropicOptions
{
    /// <summary>Server-level API key (spec decision #5). Empty ⇒ AI lookup is disabled.</summary>
    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = "claude-haiku-4-5";

    public int MaxTokens { get; set; } = 1024;
}
