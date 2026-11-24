using AnkiLearner.Core.Srs;

namespace AnkiLearner.Tests.Unit;

/// <summary>Setting up FSRS memory for cards that predate it (spec §5).</summary>
public class FsrsSetupTests
{
    private static readonly IReadOnlyList<double> W = Fsrs.DefaultWeights;
    private static readonly DateTime T0 = new(2025, 5, 10, 20, 0, 0, DateTimeKind.Utc);

    private static readonly HistoryReview[] History =
    [
        new(T0, ReviewGrade.Good, null),
        new(T0.AddDays(3), ReviewGrade.Good, 2),
        new(T0.AddDays(13), ReviewGrade.Again, 9),  // a review lapse
        new(T0.AddDays(13).AddMinutes(10), ReviewGrade.Good, null),
        new(T0.AddDays(15), ReviewGrade.Good, 1),
    ];

    private static FsrsCard Replayed => Fsrs.Replay(History.Select(h => (h.At, h.Grade)), W)!.Value;

    private static double AfterGrade(double d, ReviewGrade g) => Fsrs.Next(new MemoryState(1, d), 0, g, W).Difficulty;

    [Theory]
    [InlineData(0)]
    [InlineData(2)] // "Study again" pressed on a never-studied direction still adds lapses
    public void NeverReviewed_WithoutHistory_HasNoMemory(int lapses)
    {
        var card = new LegacyCard(IntervalDays: 0, Lapses: lapses, Repetitions: 0, LastReviewed: null, Due: T0);
        Assert.Null(FsrsSetup.MemoryFor(card, [], W));
    }

    [Fact]
    public void History_NotReviewedOnTheSite_IsTheReplay()
    {
        // The import copied Anki's interval/ease; the site never touched the card.
        var card = new LegacyCard(IntervalDays: 6, Lapses: 1, Repetitions: 5, LastReviewed: null, Due: T0.AddDays(21));

        var memory = FsrsSetup.MemoryFor(card, History, W);

        Assert.Equal(Replayed, memory);
    }

    [Fact]
    public void History_ReviewedOnTheSite_KeepsReplayDifficulty_AndTakesTheSnapshotsStabilityAndDate()
    {
        var siteReview = new DateTime(2026, 11, 20, 18, 0, 0, DateTimeKind.Utc);
        // Lapses 1 = the one Anki lapse; no lapse on the site.
        var card = new LegacyCard(IntervalDays: 40, Lapses: 1, Repetitions: 7, LastReviewed: siteReview, Due: siteReview.AddDays(40));

        var memory = FsrsSetup.MemoryFor(card, History, W)!.Value;

        Assert.Equal(Replayed.Memory.Difficulty, memory.Memory.Difficulty);
        Assert.Equal(40, memory.Memory.Stability);
        Assert.Equal(siteReview, memory.LastReviewed);
    }

    [Fact]
    public void History_WithASiteLapse_RaisesDifficulty_ThenEasesItForLaterSuccesses()
    {
        var siteReview = new DateTime(2026, 11, 20, 18, 0, 0, DateTimeKind.Utc);
        var lapsedNow = new LegacyCard(IntervalDays: 0, Lapses: 2, Repetitions: 0, LastReviewed: siteReview, Due: siteReview.AddMinutes(10));
        var recovered = lapsedNow with { IntervalDays = 6, Repetitions = 2 };

        var d = Replayed.Memory.Difficulty;
        var afterLapse = AfterGrade(d, ReviewGrade.Again);

        var lapsed = FsrsSetup.MemoryFor(lapsedNow, History, W)!.Value;
        Assert.Equal(afterLapse, lapsed.Memory.Difficulty);
        Assert.Equal(W[0], lapsed.Memory.Stability); // interval 0 → the Again starting stability

        var back = FsrsSetup.MemoryFor(recovered, History, W)!.Value;
        Assert.Equal(AfterGrade(AfterGrade(afterLapse, ReviewGrade.Good), ReviewGrade.Good), back.Memory.Difficulty);
        Assert.Equal(6, back.Memory.Stability);
    }

    [Fact]
    public void History_NewerThanTheSiteReview_WinsOverTheSnapshot()
    {
        // Reviewed once on the site, then studied on in Anki and re-uploaded.
        var siteReview = T0.AddDays(1).AddHours(2);
        var card = new LegacyCard(IntervalDays: 6, Lapses: 1, Repetitions: 2, LastReviewed: siteReview, Due: siteReview.AddDays(6));

        Assert.Equal(Replayed, FsrsSetup.MemoryFor(card, History, W));
    }

    [Fact]
    public void AnkiLapses_CountOnlyAgainOnAReviewCard()
    {
        // An Again while still learning (no interval yet) is not a lapse, so Lapses 1 here means
        // one lapse on the site.
        HistoryReview[] learningOnly = [new(T0, ReviewGrade.Again, null), new(T0.AddMinutes(1), ReviewGrade.Good, null)];
        var siteReview = new DateTime(2026, 11, 20, 18, 0, 0, DateTimeKind.Utc);
        var card = new LegacyCard(IntervalDays: 1, Lapses: 1, Repetitions: 0, LastReviewed: siteReview, Due: siteReview.AddDays(1));

        var memory = FsrsSetup.MemoryFor(card, learningOnly, W)!.Value;

        var replayD = Fsrs.Replay(learningOnly.Select(h => (h.At, h.Grade)), W)!.Value.Memory.Difficulty;
        Assert.Equal(AfterGrade(replayD, ReviewGrade.Again), memory.Memory.Difficulty);
    }

    [Fact]
    public void NoHistory_UsesTheLapseHeuristic_AndTheSnapshot()
    {
        var siteReview = new DateTime(2026, 11, 20, 18, 0, 0, DateTimeKind.Utc);
        var card = new LegacyCard(IntervalDays: 15, Lapses: 1, Repetitions: 2, LastReviewed: siteReview, Due: siteReview.AddDays(15));

        var memory = FsrsSetup.MemoryFor(card, [], W)!.Value;

        var expectedD = AfterGrade(AfterGrade(AfterGrade(Fsrs.Initial(ReviewGrade.Good, W).Difficulty,
            ReviewGrade.Again), ReviewGrade.Good), ReviewGrade.Good);
        Assert.Equal(expectedD, memory.Memory.Difficulty);
        Assert.Equal(expectedD, FsrsSetup.HeuristicDifficulty(1, 2, W));
        Assert.Equal(15, memory.Memory.Stability);
        Assert.Equal(siteReview, memory.LastReviewed);
    }

    [Fact]
    public void NoHistory_ImportedButNeverReviewedOnTheSite_DatesTheLastReviewFromTheDueDate()
    {
        // An SM-2 import without history: Anki's interval and due, no site review yet.
        var due = new DateTime(2025, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var card = new LegacyCard(IntervalDays: 30, Lapses: 0, Repetitions: 4, LastReviewed: null, Due: due);

        var memory = FsrsSetup.MemoryFor(card, [], W)!.Value;

        Assert.Equal(due.AddDays(-30), memory.LastReviewed);
        Assert.Equal(30, memory.Memory.Stability);
    }
}
