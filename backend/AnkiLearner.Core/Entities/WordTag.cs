namespace AnkiLearner.Core.Entities;

/// <summary>M:N link between words and tags. Composite key (WordId, TagId).</summary>
public class WordTag
{
    public Guid WordId { get; set; }
    public Word Word { get; set; } = null!;
    public Guid TagId { get; set; }
    public Tag Tag { get; set; } = null!;
}
