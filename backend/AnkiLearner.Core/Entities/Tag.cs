namespace AnkiLearner.Core.Entities;

/// <summary>Free-form label owned by a user. Name unique per user.</summary>
public class Tag
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;

    public List<WordTag> WordTags { get; set; } = [];
}
