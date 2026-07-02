namespace AnkiLearner.Core;

public record Language(string Code, string Name);

/// <summary>Fixed catalog of selectable languages (spec FR-S5). BCP-47 codes, English names.</summary>
public static class LanguageCatalog
{
    public static readonly IReadOnlyList<Language> All =
    [
        new("ar", "Arabic"),
        new("bg", "Bulgarian"),
        new("cs", "Czech"),
        new("da", "Danish"),
        new("de", "German"),
        new("el", "Greek"),
        new("en", "English"),
        new("es", "Spanish"),
        new("et", "Estonian"),
        new("fi", "Finnish"),
        new("fr", "French"),
        new("he", "Hebrew"),
        new("hi", "Hindi"),
        new("hr", "Croatian"),
        new("hu", "Hungarian"),
        new("id", "Indonesian"),
        new("it", "Italian"),
        new("ja", "Japanese"),
        new("ko", "Korean"),
        new("lt", "Lithuanian"),
        new("lv", "Latvian"),
        new("nb", "Norwegian (Bokmål)"),
        new("nl", "Dutch"),
        new("pl", "Polish"),
        new("pt", "Portuguese"),
        new("ro", "Romanian"),
        new("ru", "Russian"),
        new("sk", "Slovak"),
        new("sl", "Slovenian"),
        new("sr", "Serbian"),
        new("sv", "Swedish"),
        new("th", "Thai"),
        new("tr", "Turkish"),
        new("uk", "Ukrainian"),
        new("vi", "Vietnamese"),
        new("zh", "Chinese"),
    ];

    public static bool IsValid(string code) => All.Any(l => l.Code == code);
}
