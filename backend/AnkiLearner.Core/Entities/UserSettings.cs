using AnkiLearner.Core.Srs;

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

    /// <summary>Ceiling on the SRS interval, in days. Caps runaway intervals on mature cards.</summary>
    public int MaxIntervalDays { get; set; } = Fsrs.DefaultMaxIntervalDays;

    /// <summary>Target probability of recalling a card when it comes due (FSRS). Lower = fewer
    /// reviews, more forgetting.</summary>
    public double DesiredRetention { get; set; } = Fsrs.DefaultDesiredRetention;

    /// <summary>The user's own 21 FSRS weights (e.g. fitted by Anki's optimizer); null = defaults.</summary>
    public double[]? FsrsWeights { get; set; }

    /// <summary>The scheduler settings these preferences describe.</summary>
    public FsrsParameters SchedulerParameters() =>
        new(FsrsWeights ?? Fsrs.DefaultWeights, DesiredRetention, MaxIntervalDays);

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
