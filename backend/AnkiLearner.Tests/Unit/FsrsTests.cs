using System.Globalization;
using AnkiLearner.Core.Entities;
using AnkiLearner.Core.Srs;
using static AnkiLearner.Tests.Unit.FsrsReferenceVectors;

namespace AnkiLearner.Tests.Unit;

/// <summary>Verifies the FSRS-6 port against reference values (spec §5).</summary>
public class FsrsTests
{
    private static readonly DateTime Now = new(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc);

    public static TheoryData<string> VectorNames()
    {
        var data = new TheoryData<string>();
        foreach (var v in All) data.Add(v.Name);
        return data;
    }

    private static Vector Find(string name) => All.Single(v => v.Name == name);

    private static DateTime Utc(string at) =>
        DateTime.SpecifyKind(DateTime.Parse(at, CultureInfo.InvariantCulture), DateTimeKind.Utc);

    private static void AssertClose(double expected, double actual) =>
        Assert.True(Math.Abs(expected - actual) <= 1e-9 * Math.Max(1, Math.Abs(expected)),
            $"expected {expected}, got {actual}");

    private static void AssertUnit(double u) => Assert.True(u >= 0 && u < 1, $"{u} is not in [0, 1)");

    // --- reference vectors ---

    [Theory]
    [MemberData(nameof(VectorNames))]
    public void EachStep_MatchesReference(string name)
    {
        var v = Find(name);
        FsrsCard? card = null;
        foreach (var step in v.Steps)
        {
            var at = Utc(step.At);
            MemoryState memory;
            if (card is { } c)
            {
                var elapsed = Fsrs.ElapsedDays(c.LastReviewed, at);
                Assert.Equal(step.Elapsed, elapsed);
                AssertClose(step.R!.Value, Fsrs.Retrievability(c.Memory, elapsed, v.Weights));
                memory = Fsrs.Next(c.Memory, elapsed, step.Grade, v.Weights);
            }
            else
            {
                Assert.Null(step.Elapsed);
                memory = Fsrs.Initial(step.Grade, v.Weights);
            }
            AssertClose(step.S, memory.Stability);
            AssertClose(step.D, memory.Difficulty);
            card = new FsrsCard(memory, at);
        }
    }

    [Theory]
    [MemberData(nameof(VectorNames))]
    public void Replay_EndsInTheReferenceState(string name)
    {
        var v = Find(name);
        var card = Fsrs.Replay(v.Steps.Select(s => (Utc(s.At), s.Grade)), v.Weights);

        Assert.NotNull(card);
        AssertClose(v.Steps[^1].S, card.Value.Memory.Stability);
        AssertClose(v.Steps[^1].D, card.Value.Memory.Difficulty);
        Assert.Equal(Utc(v.Steps[^1].At), card.Value.LastReviewed);
    }

    [Theory]
    [MemberData(nameof(VectorNames))]
    public void NextInterval_MatchesReference(string name)
    {
        var v = Find(name);
        var s = v.Steps[^1].S;
        Assert.Equal(v.Interval90, Fsrs.NextInterval(s, new FsrsParameters(v.Weights, 0.90, 36500)));
        Assert.Equal(v.Interval85, Fsrs.NextInterval(s, new FsrsParameters(v.Weights, 0.85, 36500)));
        Assert.Equal(v.Interval85Cap30, Fsrs.NextInterval(s, new FsrsParameters(v.Weights, 0.85, 30)));
    }

    [Fact]
    public void Replay_SortsItsInputByTime()
    {
        var v = Find("Default/MixedHistory");
        var shuffled = v.Steps.Select(s => (Utc(s.At), s.Grade)).Reverse();

        var card = Fsrs.Replay(shuffled, v.Weights);

        AssertClose(v.Steps[^1].S, card!.Value.Memory.Stability);
        AssertClose(v.Steps[^1].D, card.Value.Memory.Difficulty);
    }

    [Fact]
    public void Replay_OfNothing_IsNull() =>
        Assert.Null(Fsrs.Replay([], Fsrs.DefaultWeights));

    /// <summary>A real card from an Anki collection: 61 reviews with 6 lapses and many same-day
    /// steps. Grades and day offsets only, dates shifted to 2030. Expected values are Anki 26.09's
    /// own <c>compute_memory_state</c> with the default weights (single precision, hence 1e-4).</summary>
    [Fact]
    public void Replay_OfARealAnkiCard_MatchesAnki()
    {
        (string At, ReviewGrade Grade)[] history =
        [
        ("2030-01-01T19:52", ReviewGrade.Again),
        ("2030-01-01T19:53", ReviewGrade.Hard),
        ("2030-01-01T20:00", ReviewGrade.Again),
        ("2030-01-01T20:01", ReviewGrade.Again),
        ("2030-01-01T20:03", ReviewGrade.Hard),
        ("2030-01-01T20:06", ReviewGrade.Again),
        ("2030-01-01T20:06", ReviewGrade.Hard),
        ("2030-01-01T20:07", ReviewGrade.Hard),
        ("2030-01-01T20:08", ReviewGrade.Good),
        ("2030-01-01T20:08", ReviewGrade.Good),
        ("2030-01-02T15:38", ReviewGrade.Again),
        ("2030-01-02T15:45", ReviewGrade.Again),
        ("2030-01-02T15:48", ReviewGrade.Again),
        ("2030-01-02T15:48", ReviewGrade.Again),
        ("2030-01-02T15:49", ReviewGrade.Good),
        ("2030-01-04T16:38", ReviewGrade.Again),
        ("2030-01-04T16:49", ReviewGrade.Again),
        ("2030-01-04T16:54", ReviewGrade.Again),
        ("2030-01-04T16:56", ReviewGrade.Again),
        ("2030-01-04T16:56", ReviewGrade.Hard),
        ("2030-01-04T16:57", ReviewGrade.Again),
        ("2030-01-04T16:57", ReviewGrade.Again),
        ("2030-01-04T16:58", ReviewGrade.Again),
        ("2030-01-04T16:58", ReviewGrade.Again),
        ("2030-01-04T16:58", ReviewGrade.Again),
        ("2030-01-04T16:59", ReviewGrade.Hard),
        ("2030-01-04T16:59", ReviewGrade.Again),
        ("2030-01-04T16:59", ReviewGrade.Again),
        ("2030-01-04T16:59", ReviewGrade.Hard),
        ("2030-01-04T17:00", ReviewGrade.Again),
        ("2030-01-04T17:00", ReviewGrade.Hard),
        ("2030-01-04T17:00", ReviewGrade.Hard),
        ("2030-01-04T17:01", ReviewGrade.Hard),
        ("2030-01-04T17:02", ReviewGrade.Hard),
        ("2030-01-04T17:02", ReviewGrade.Hard),
        ("2030-01-04T17:02", ReviewGrade.Hard),
        ("2030-01-04T17:03", ReviewGrade.Hard),
        ("2030-01-04T17:03", ReviewGrade.Good),
        ("2030-01-06T18:59", ReviewGrade.Again),
        ("2030-01-06T19:10", ReviewGrade.Again),
        ("2030-01-06T19:16", ReviewGrade.Again),
        ("2030-01-06T19:17", ReviewGrade.Hard),
        ("2030-01-06T19:17", ReviewGrade.Hard),
        ("2030-01-06T19:17", ReviewGrade.Good),
        ("2030-01-07T17:29", ReviewGrade.Again),
        ("2030-01-07T17:33", ReviewGrade.Good),
        ("2030-01-08T20:20", ReviewGrade.Hard),
        ("2030-01-10T12:53", ReviewGrade.Hard),
        ("2030-01-14T09:31", ReviewGrade.Again),
        ("2030-01-14T09:41", ReviewGrade.Good),
        ("2030-01-16T10:30", ReviewGrade.Hard),
        ("2030-01-18T17:14", ReviewGrade.Hard),
        ("2030-01-23T19:47", ReviewGrade.Good),
        ("2030-02-13T07:56", ReviewGrade.Again),
        ("2030-02-13T08:05", ReviewGrade.Good),
        ("2030-02-15T22:32", ReviewGrade.Good),
        ("2030-02-19T08:11", ReviewGrade.Good),
        ("2030-02-24T08:01", ReviewGrade.Hard),
        ("2030-03-03T18:32", ReviewGrade.Hard),
        ("2030-03-13T20:59", ReviewGrade.Good),
        ("2030-03-24T19:39", ReviewGrade.Hard),
        ];

        var card = Fsrs.Replay(history.Select(h => (Utc(h.At), h.Grade)), Fsrs.DefaultWeights);

        Assert.NotNull(card);
        Assert.Equal(11.979376792907715, card.Value.Memory.Stability, 1e-4 * 12);
        Assert.Equal(9.936247825622559, card.Value.Memory.Difficulty, 1e-4 * 10);
    }

    // --- elapsed days ---

    [Fact]
    public void ElapsedDays_CountsCalendarDays_NotHours()
    {
        var lateEvening = new DateTime(2026, 1, 1, 23, 50, 0, DateTimeKind.Utc);
        Assert.Equal(1, Fsrs.ElapsedDays(lateEvening, lateEvening.AddMinutes(20))); // past midnight
        Assert.Equal(0, Fsrs.ElapsedDays(lateEvening.AddHours(-13), lateEvening)); // same day, 13 h
        Assert.Equal(0, Fsrs.ElapsedDays(lateEvening, lateEvening.AddDays(-1)));   // clock skew → 0
    }

    [Fact]
    public void ElapsedDays_TreatsALocalTimeAsTheInstantItIs()
    {
        var utc = new DateTime(2026, 1, 1, 23, 50, 0, DateTimeKind.Utc);
        Assert.Equal(Fsrs.ElapsedDays(utc, utc.AddMinutes(20)),
            Fsrs.ElapsedDays(utc.ToLocalTime(), utc.AddMinutes(20).ToLocalTime()));
    }

    [Fact]
    public void NextInterval_WithNaNRetention_FallsBackToOneDay() =>
        Assert.Equal(1, Fsrs.NextInterval(10, FsrsParameters.Default with { DesiredRetention = double.NaN }));

    // --- Review ---

    [Fact]
    public void Review_FirstGrade_HasNoRetrievability_AndUsesInitialMemory()
    {
        var review = Fsrs.Review(null, ReviewGrade.Good, Now, FsrsParameters.Default, fuzzUnit: 0.5);

        Assert.Null(review.Retrievability);
        Assert.Equal(Fsrs.Initial(ReviewGrade.Good, Fsrs.DefaultWeights), review.Memory);
        Assert.Equal(2, review.IntervalDays); // S 2.31 at 90% → 2 days, too short to fuzz
        Assert.Equal(Now.AddDays(2), review.Due);
    }

    [Fact]
    public void Review_Again_ComesBackInTenMinutes_WithAZeroInterval()
    {
        var card = new FsrsCard(new MemoryState(40, 5), Now.AddDays(-30));
        var review = Fsrs.Review(card, ReviewGrade.Again, Now, FsrsParameters.Default, fuzzUnit: 0.5);

        Assert.Equal(0, review.IntervalDays);
        Assert.Equal(Now.AddMinutes(Fsrs.AgainMinutes), review.Due);
        Assert.True(review.Memory.Stability < 40);
        Assert.NotNull(review.Retrievability);
    }

    [Fact]
    public void Review_SchedulesTheFuzzedInterval()
    {
        var card = new FsrsCard(new MemoryState(40, 5), Now.AddDays(-40));
        var p = FsrsParameters.Default;
        var memory = Fsrs.Next(card.Memory, 40, ReviewGrade.Good, p.Weights);
        var plain = Fsrs.NextInterval(memory.Stability, p);

        var low = Fsrs.Review(card, ReviewGrade.Good, Now, p, fuzzUnit: 0.0);
        var high = Fsrs.Review(card, ReviewGrade.Good, Now, p, fuzzUnit: 0.999999);

        Assert.Equal(Fsrs.Fuzz(plain, 0.0, p.MaxIntervalDays), low.IntervalDays);
        Assert.Equal(Fsrs.Fuzz(plain, 0.999999, p.MaxIntervalDays), high.IntervalDays);
        Assert.True(low.IntervalDays < plain && plain < high.IntervalDays);
        Assert.Equal(Now.AddDays(high.IntervalDays), high.Due);
    }

    [Fact]
    public void Review_HardGoodEasy_GiveIncreasingIntervals()
    {
        var card = new FsrsCard(new MemoryState(20, 5), Now.AddDays(-20));
        int Days(ReviewGrade g) => Fsrs.Review(card, g, Now, FsrsParameters.Default, 0.5).IntervalDays;

        Assert.True(Days(ReviewGrade.Hard) < Days(ReviewGrade.Good));
        Assert.True(Days(ReviewGrade.Good) < Days(ReviewGrade.Easy));
    }

    [Fact]
    public void Review_RightAfterAgain_StillGivesThreeDifferentIntervals()
    {
        // Same-day relearning: S is tiny, so every passing grade would round to 1 day.
        var card = new FsrsCard(new MemoryState(0.08, 8), Now.AddMinutes(-10));
        int Days(ReviewGrade g) => Fsrs.Review(card, g, Now, FsrsParameters.Default, 0.5).IntervalDays;

        Assert.Equal(1, Days(ReviewGrade.Hard));
        Assert.Equal(2, Days(ReviewGrade.Good));
        Assert.Equal(3, Days(ReviewGrade.Easy));
    }

    [Fact]
    public void Review_OrderedIntervals_RespectTheMaximum()
    {
        var card = new FsrsCard(new MemoryState(0.08, 8), Now.AddMinutes(-10));
        var p = FsrsParameters.Default with { MaxIntervalDays = 1 };

        Assert.All(new[] { ReviewGrade.Hard, ReviewGrade.Good, ReviewGrade.Easy },
            g => Assert.Equal(1, Fsrs.Review(card, g, Now, p, 0.5).IntervalDays));
    }

    [Fact]
    public void Review_LongOverdueSuccess_GrowsStabilityMoreThanAnOnTimeOne()
    {
        // The case for learners who skip sessions: a late success proves more.
        var memory = new MemoryState(30, 5);
        var onTime = Fsrs.Next(memory, 30, ReviewGrade.Good, Fsrs.DefaultWeights);
        var late = Fsrs.Next(memory, 120, ReviewGrade.Good, Fsrs.DefaultWeights);

        Assert.True(late.Stability > onTime.Stability);
    }

    [Fact]
    public void Review_CapsTheIntervalAtTheMaximum()
    {
        var card = new FsrsCard(new MemoryState(5000, 1), Now.AddDays(-5000));
        var p = FsrsParameters.Default with { MaxIntervalDays = 180 };

        var review = Fsrs.Review(card, ReviewGrade.Easy, Now, p, fuzzUnit: 0.999999);

        Assert.Equal(180, review.IntervalDays);
    }

    // --- Study again ---

    [Fact]
    public void Forget_DropsStabilityLikeAgain_ButKeepsDifficulty()
    {
        var memory = new MemoryState(60, 4.2);
        var forgotten = Fsrs.Forget(memory, 45, Fsrs.DefaultWeights);
        var again = Fsrs.Next(memory, 45, ReviewGrade.Again, Fsrs.DefaultWeights);

        Assert.Equal(again.Stability, forgotten.Stability);
        Assert.Equal(4.2, forgotten.Difficulty);
        Assert.True(again.Difficulty > 4.2); // a real Again would have raised it
    }

    // --- fuzz ---

    [Theory]
    // Ranges computed with py-fsrs 6.3.3 FUZZ_RANGES.
    [InlineData(3, 2, 4)]
    [InlineData(5, 4, 6)]
    [InlineData(7, 5, 9)]
    [InlineData(10, 8, 12)]
    [InlineData(20, 17, 23)]
    [InlineData(50, 46, 54)]
    [InlineData(100, 93, 107)]
    [InlineData(365, 345, 385)]
    [InlineData(1000, 948, 1052)]
    public void Fuzz_StaysInPyFsrsRange(int interval, int min, int max)
    {
        Assert.Equal(min, Fsrs.Fuzz(interval, 0.0, 36500));
        Assert.Equal(max, Fsrs.Fuzz(interval, 0.999999, 36500));
        for (var u = 0.0; u < 1; u += 0.01)
            Assert.InRange(Fsrs.Fuzz(interval, u, 36500), min, max);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Fuzz_LeavesShortIntervalsAlone(int interval) =>
        Assert.Equal(interval, Fsrs.Fuzz(interval, 0.999999, 36500));

    [Fact]
    public void Fuzz_StaysInRange_EvenForAUnitOfOne() =>
        Assert.Equal(107, Fsrs.Fuzz(100, 1.0, 36500));

    [Fact]
    public void Review_RejectsAnUndefinedGrade() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Fsrs.Review(null, (ReviewGrade)7, Now, FsrsParameters.Default, 0.5));

    [Fact]
    public void Fuzz_NeverExceedsTheMaximum() =>
        Assert.Equal(100, Fsrs.Fuzz(100, 0.999999, 100));

    [Fact]
    public void FuzzUnit_IsStable_InRange_AndVariesByCard()
    {
        var word = Guid.Parse("5f0d3c6e-8a1b-4c2d-9e3f-1a2b3c4d5e6f");
        var last = new DateTime(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc);

        var u = Fsrs.FuzzUnit(word, ExerciseType.TargetToKnown, last);

        Assert.Equal(u, Fsrs.FuzzUnit(word, ExerciseType.TargetToKnown, last)); // preview == apply
        AssertUnit(u);
        Assert.NotEqual(u, Fsrs.FuzzUnit(word, ExerciseType.KnownToTarget, last));
        Assert.NotEqual(u, Fsrs.FuzzUnit(word, ExerciseType.TargetToKnown, last.AddDays(1)));
        Assert.NotEqual(u, Fsrs.FuzzUnit(Guid.NewGuid(), ExerciseType.TargetToKnown, last));
        AssertUnit(Fsrs.FuzzUnit(word, ExerciseType.TargetToKnown, null));
    }

    [Fact]
    public void FuzzUnit_IgnoresSubMillisecondTicks_LostInTheDatabase()
    {
        var word = Guid.NewGuid();
        var inMemory = new DateTime(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc).AddTicks(1234567); // 123.4567 ms
        var reloaded = new DateTime(inMemory.Ticks - inMemory.Ticks % 10, DateTimeKind.Utc); // µs precision

        Assert.Equal(Fsrs.FuzzUnit(word, ExerciseType.TargetToKnown, inMemory),
            Fsrs.FuzzUnit(word, ExerciseType.TargetToKnown, reloaded));
    }

    [Fact]
    public void FuzzUnit_SpreadsAcrossTheRange()
    {
        var units = Enumerable.Range(0, 1000)
            .Select(i => Fsrs.FuzzUnit(Guid.NewGuid(), ExerciseType.TargetToKnown, Now.AddDays(i)))
            .ToList();
        Assert.All(units, AssertUnit);
        Assert.InRange(units.Average(), 0.45, 0.55);
    }

    // --- weights ---

    [Fact]
    public void Validate_AcceptsTheDefaults() => Assert.Null(Fsrs.Validate(Fsrs.DefaultWeights));

    [Fact]
    public void Validate_AcceptsTheCustomTestWeights() => Assert.Null(Fsrs.Validate(CustomWeights));

    [Fact]
    public void Validate_RejectsTheWrongCount()
    {
        var error = Fsrs.Validate(Fsrs.DefaultWeights.Take(19).ToArray());
        Assert.Equal("Expected 21 FSRS weights, got 19.", error);
    }

    [Theory]
    [InlineData(0, 0.0)]        // initial stability must be ≥ 0.001
    [InlineData(7, 0.9)]        // mean reversion ≤ 0.75
    [InlineData(20, 0.05)]      // decay ≥ 0.1
    [InlineData(4, double.NaN)]
    public void Validate_RejectsOutOfBoundsValues(int index, double value)
    {
        var weights = Fsrs.DefaultWeights.ToArray();
        weights[index] = value;
        var error = Fsrs.Validate(weights);
        Assert.NotNull(error);
        Assert.StartsWith($"FSRS weight {index} = ", error);
    }
}
