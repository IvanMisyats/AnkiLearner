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
    public void Parse_ReadsEachCardsGradedReviews_OldestFirst()
    {
        var t = new DateTime(2025, 5, 10, 19, 0, 0, DateTimeKind.Utc);
        var apkg = ApkgFixture.Build(
            Crt,
            notes: [new FixtureNote(1, "hund", "dog")],
            cards:
            [
                new FixtureCard(NoteId: 1, Ord: 0, Type: 2, Queue: 2, Due: 200, Ivl: 30, Factor: 2500, Reps: 3, Lapses: 0),
                new FixtureCard(NoteId: 1, Ord: 1, Type: 2, Queue: 2, Due: 210, Ivl: 20, Factor: 2300, Reps: 2, Lapses: 0),
            ],
            reviews:
            [
                new FixtureReview(0, t.AddDays(3), Ease: 3, Ivl: 7, LastIvl: 1),
                new FixtureReview(0, t, Ease: 1, Ivl: -600, LastIvl: 0, Factor: 0, Type: 0),   // learning step
                new FixtureReview(0, t.AddMinutes(10), Ease: 3, Ivl: 1, LastIvl: -600, Factor: 2500, Type: 0),
                new FixtureReview(0, t.AddDays(5), Ease: 0, Ivl: 30, LastIvl: 7, Type: 4),     // manual reschedule
                new FixtureReview(1, t.AddDays(1), Ease: 4, Ivl: 4, LastIvl: 0, Type: 0),
            ]);

        var note = Assert.Single(ApkgParser.Parse(new MemoryStream(apkg)).Notes);

        var forward = note.Cards.Single(c => c.Ord == 0).Reviews;
        Assert.Equal([1, 3, 3], forward.Select(r => r.Ease));
        Assert.Equal(new DateTimeOffset(t).ToUnixTimeMilliseconds(), forward[0].Id);
        Assert.Equal(-600, forward[0].IntervalDays);
        Assert.Equal(1, forward[2].LastIntervalDays);
        Assert.Equal(7, forward[2].IntervalDays);
        Assert.Equal(1, forward[2].Type);

        var backward = Assert.Single(note.Cards.Single(c => c.Ord == 1).Reviews);
        Assert.Equal(4, backward.Ease);
    }

    [Fact]
    public void Parse_AnkiForget_WipesTheEarlierReviews()
    {
        var t = new DateTime(2025, 5, 10, 19, 0, 0, DateTimeKind.Utc);
        var apkg = ApkgFixture.Build(
            Crt,
            notes: [new FixtureNote(1, "hund", "dog")],
            cards: [new FixtureCard(NoteId: 1, Ord: 0, Type: 2, Queue: 2, Due: 200, Ivl: 3, Factor: 2500, Reps: 5, Lapses: 0)],
            reviews:
            [
                new FixtureReview(0, t, Ease: 3),
                new FixtureReview(0, t.AddDays(2), Ease: 3),
                new FixtureReview(0, t.AddDays(30), Ease: 0, Ivl: 0, LastIvl: 20, Factor: 0, Type: 4), // Forget
                new FixtureReview(0, t.AddDays(31), Ease: 3, Ivl: 3, LastIvl: 0, Type: 0),
            ]);

        var reviews = Assert.Single(Assert.Single(ApkgParser.Parse(new MemoryStream(apkg)).Notes).Cards).Reviews;

        var only = Assert.Single(reviews);
        Assert.Equal(new DateTimeOffset(t.AddDays(31)).ToUnixTimeMilliseconds(), only.Id);
    }

    [Fact]
    public void Parse_SkipsImpossibleDates_AndFilteredReviewsThatDidNotReschedule()
    {
        var t = new DateTime(2025, 5, 10, 19, 0, 0, DateTimeKind.Utc);
        var apkg = ApkgFixture.Build(
            Crt,
            notes: [new FixtureNote(1, "hund", "dog")],
            cards: [new FixtureCard(NoteId: 1, Ord: 0, Type: 2, Queue: 2, Due: 200, Ivl: 3, Factor: 2500, Reps: 5, Lapses: 0)],
            reviews:
            [
                new FixtureReview(0, t, Ease: 3),
                new FixtureReview(0, t.AddDays(1), Ease: 3, Factor: 0, Type: 3),    // cram, no rescheduling
                new FixtureReview(0, t.AddDays(2), Ease: 3, Factor: 2500, Type: 3), // filtered, rescheduled
            ],
            // 1970 and the year 292 million: not real reviews.
            extraSql: "INSERT INTO revlog VALUES (1, 1, -1, 3, 1, 0, 2500, 5000, 1); " +
                      "INSERT INTO revlog VALUES (9223372036854775807, 1, -1, 3, 1, 0, 2500, 5000, 1);");

        var result = ApkgParser.Parse(new MemoryStream(apkg));

        var reviews = Assert.Single(Assert.Single(result.Notes).Cards).Reviews;
        Assert.Equal(2, reviews.Count);
        Assert.Equal([1, 3], reviews.Select(r => r.Type));
        Assert.Empty(result.Skipped);
    }

    [Fact]
    public void Parse_MalformedRevlog_LeavesTheHistoryOut_ButKeepsTheWords()
    {
        var t = new DateTime(2025, 5, 10, 19, 0, 0, DateTimeKind.Utc);
        var apkg = ApkgFixture.Build(
            Crt,
            notes: [new FixtureNote(1, "hund", "dog")],
            cards: [new FixtureCard(NoteId: 1, Ord: 0, Type: 2, Queue: 2, Due: 200, Ivl: 3, Factor: 2500, Reps: 5, Lapses: 0)],
            reviews: [new FixtureReview(0, t, Ease: 3)],
            extraSql: $"INSERT INTO revlog VALUES ({new DateTimeOffset(t.AddDays(1)).ToUnixTimeMilliseconds()}, 1, -1, 3, 1, 0, NULL, 5000, 1);");

        var result = ApkgParser.Parse(new MemoryStream(apkg));

        var note = Assert.Single(result.Notes);
        Assert.Empty(Assert.Single(note.Cards).Reviews);
        Assert.Contains(result.Skipped, s => s.Contains("Review history could not be read"));
    }

    [Fact]
    public void Parse_WithoutARevlogTable_GivesCardsNoReviews()
    {
        var apkg = ApkgFixture.Build(
            Crt,
            notes: [new FixtureNote(1, "kat", "cat")],
            cards: [new FixtureCard(NoteId: 1, Ord: 0, Type: 2, Queue: 2, Due: 200, Ivl: 30, Factor: 2500, Reps: 3, Lapses: 0)]);

        var card = Assert.Single(Assert.Single(ApkgParser.Parse(new MemoryStream(apkg)).Notes).Cards);

        Assert.Empty(card.Reviews);
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
