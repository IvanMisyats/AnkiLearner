using AnkiLearner.Infrastructure.Anki;
using AnkiLearner.Tests.Fixtures;

namespace AnkiLearner.Tests.Unit;

public class ApkgParserTests
{
    private static readonly long Crt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
        .ToUnixTimeSeconds();

    [Fact]
    public void Parse_ReadsNotesCardsDecksAndTags()
    {
        var apkg = ApkgFixture.Build(
            Crt,
            notes: [new FixtureNote(1, "hund", "dog<br>hound", Tags: " animals  common ")],
            cards:
            [
                new FixtureCard(NoteId: 1, Ord: 0, Type: 2, Queue: 2, Due: 200, Ivl: 30, Factor: 2500, Reps: 12, Lapses: 1),
                new FixtureCard(NoteId: 1, Ord: 1, Type: 0, Queue: 0, Due: 5, Ivl: 0, Factor: 0, Reps: 0, Lapses: 0),
            ],
            deckName: "Danish");

        var result = ApkgParser.Parse(new MemoryStream(apkg));

        var note = Assert.Single(result.Notes);
        Assert.Equal("hund", note.Front);
        Assert.Equal("dog<br>hound", note.Back);
        Assert.Equal(["animals", "common"], note.Tags);
        Assert.Equal("Danish", note.DeckName);
        Assert.Equal(2, note.Cards.Count);
        Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), result.CollectionCreatedUtc);

        var review = note.Cards.Single(c => c.Ord == 0);
        Assert.Equal(2, review.Type);
        Assert.Equal(30, review.IntervalDays);
        Assert.Equal(2500, review.EaseFactor);
        Assert.Equal(12, review.Reps);
    }

    [Fact]
    public void Parse_SkipsNotesWithMissingFields()
    {
        var apkg = ApkgFixture.Build(
            Crt,
            notes:
            [
                new FixtureNote(1, "ok", "fine"),
                new FixtureNote(2, "", "no front side"),
            ],
            cards: []);

        var result = ApkgParser.Parse(new MemoryStream(apkg));

        Assert.Single(result.Notes);
        var skipped = Assert.Single(result.Skipped);
        Assert.Contains("Note 2", skipped);
    }

    [Fact]
    public void Parse_RejectsLegacyPackages_WithClearMessage()
    {
        var legacy = ApkgFixture.BuildLegacy();
        var ex = Assert.Throws<ApkgFormatException>(() => ApkgParser.Parse(new MemoryStream(legacy)));
        Assert.Contains("Re-export", ex.Message);
    }
}
