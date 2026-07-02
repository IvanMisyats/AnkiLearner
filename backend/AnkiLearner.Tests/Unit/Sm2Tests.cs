using AnkiLearner.Core.Entities;
using AnkiLearner.Core.Srs;

namespace AnkiLearner.Tests.Unit;

/// <summary>Verifies the SM-2 port matches the POC/spec §5 exactly.</summary>
public class Sm2Tests
{
    private static readonly DateTime Now = new(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc);

    private static SrsState Fresh() => new();

    [Fact]
    public void Good_FirstThreeReviews_Follow_1_6_TimesEase_Ladder()
    {
        var s = Fresh();

        Sm2.Apply(s, ReviewGrade.Good, Now);
        Assert.Equal(1, s.IntervalDays);
        Assert.Equal(1, s.Repetitions);
        Assert.Equal(2.5, s.EaseFactor, precision: 10); // Good (q=4) keeps ease at 2.5
        Assert.Equal(Now.AddDays(1), s.Due);

        Sm2.Apply(s, ReviewGrade.Good, Now);
        Assert.Equal(6, s.IntervalDays);

        Sm2.Apply(s, ReviewGrade.Good, Now);
        Assert.Equal(15, s.IntervalDays); // round(6 * 2.5)
        Assert.Equal(3, s.Repetitions);
    }

    [Fact]
    public void Again_ResetsProgress_AndReschedulesInTenMinutes()
    {
        var s = Fresh();
        Sm2.Apply(s, ReviewGrade.Good, Now);
        Sm2.Apply(s, ReviewGrade.Good, Now);

        Sm2.Apply(s, ReviewGrade.Again, Now);

        Assert.Equal(0, s.Repetitions);
        Assert.Equal(0, s.IntervalDays);
        Assert.Equal(1, s.Lapses);
        Assert.Equal(Now.AddMinutes(10), s.Due);
        Assert.Equal(2.5 - 0.32, s.EaseFactor, precision: 10); // q=2 → -0.32
    }

    [Fact]
    public void Hard_CountsAsSuccess_ButLowersEase()
    {
        var s = Fresh();
        Sm2.Apply(s, ReviewGrade.Hard, Now);

        Assert.Equal(1, s.Repetitions);
        Assert.Equal(1, s.IntervalDays);
        Assert.Equal(2.5 - 0.14, s.EaseFactor, precision: 10); // q=3 → -0.14
    }

    [Fact]
    public void Easy_RaisesEase()
    {
        var s = Fresh();
        Sm2.Apply(s, ReviewGrade.Easy, Now);
        Assert.Equal(2.6, s.EaseFactor, precision: 10); // q=5 → +0.1
    }

    [Fact]
    public void EaseFactor_NeverDropsBelowFloor()
    {
        var s = Fresh();
        for (var i = 0; i < 10; i++)
            Sm2.Apply(s, ReviewGrade.Again, Now);
        Assert.Equal(1.3, s.EaseFactor);
    }

    [Fact]
    public void PreviewDays_MatchesApply_WithoutMutating()
    {
        var s = Fresh();
        Sm2.Apply(s, ReviewGrade.Good, Now);
        var snapshot = (s.IntervalDays, s.EaseFactor, s.Repetitions, s.Lapses, s.Due);

        var preview = Sm2.PreviewDays(s, ReviewGrade.Good);

        Assert.Equal(6, preview);
        Assert.Equal(snapshot, (s.IntervalDays, s.EaseFactor, s.Repetitions, s.Lapses, s.Due));
    }

    [Fact]
    public void MatureInterval_MultipliesByEase_AndNeverShrinksBelowOneDay()
    {
        var s = new SrsState { IntervalDays = 100, EaseFactor = 1.3, Repetitions = 5 };
        Sm2.Apply(s, ReviewGrade.Good, Now);
        Assert.Equal(130, s.IntervalDays);

        var tiny = new SrsState { IntervalDays = 0, EaseFactor = 1.3, Repetitions = 5 };
        Sm2.Apply(tiny, ReviewGrade.Good, Now);
        Assert.Equal(1, tiny.IntervalDays); // Math.Max(1, …) guard
    }
}
