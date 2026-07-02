namespace AnkiLearner.Core.Entities;

/// <summary>Per-user preferences. Created with defaults at registration (spec §3.2).</summary>
public class UserSettings
{
    public Guid UserId { get; set; }

    /// <summary>BCP-47 code of the language being learned, e.g. "da".</summary>
    public string LearningLanguage { get; set; } = "da";

    /// <summary>Ordered BCP-47 codes; the first is the primary known language.</summary>
    public List<string> KnownLanguages { get; set; } = ["en"];

    /// <summary>Max new words introduced per day per exercise. 0 = unlimited.</summary>
    public int DailyNewLimit { get; set; } = 20;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
