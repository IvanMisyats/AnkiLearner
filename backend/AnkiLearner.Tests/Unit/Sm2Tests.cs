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
    public void Easy_OnNewCard_GraduatesToTheEasyInterval()
    {
        var s = Fresh();
        Sm2.Apply(s, ReviewGrade.Easy, Now);
        Assert.Equal(4, s.IntervalDays); // Anki's easy interval, vs 1 d for Good/Hard
    }

    // The bug this scheme fixes: textbook SM-2 gave Hard/Good/Easy the same interval,
    // so the three buttons were indistinguishable on every mature card.
    [Fact]
    public void MatureCard_Hard_Good_Easy_AllDiffer()
    {
        var s = new SrsState { IntervalDays = 80, EaseFactor = 2.5, Repetitions = 5 };

        Assert.Equal(96, Sm2.PreviewDays(s, ReviewGrade.Hard));  // 80 × 1.2
        Assert.Equal(200, Sm2.PreviewDays(s, ReviewGrade.Good)); // 80 × 2.5
        Assert.Equal(260, Sm2.PreviewDays(s, ReviewGrade.Easy)); // 80 × 2.5 × 1.3
    }

    [Fact]
    public void Hard_StaysBelowGood_EvenAtTheEaseFloor()
    {
        // 1.3 is the lowest ease a card can reach — the narrowest Hard/Good gap possible.
        // The Math.Min in Apply is the belt-and-braces guard behind this.
        var s = new SrsState { IntervalDays = 10, EaseFactor = 1.3, Repetitions = 5 };

        var hard = Sm2.PreviewDays(s, ReviewGrade.Hard);
        var good = Sm2.PreviewDays(s, ReviewGrade.Good);
        Assert.Equal(12, hard);
        Assert.Equal(13, good);
        Assert.True(hard <= good);
    }

    [Fact]
    public void MaxIntervalDays_CapsEveryPassingGrade()
    {
        var s = new SrsState { IntervalDays = 200, EaseFactor = 2.5, Repetitions = 5 };

        Assert.Equal(180, Sm2.PreviewDays(s, ReviewGrade.Good, maxIntervalDays: 180));
        Assert.Equal(180, Sm2.PreviewDays(s, ReviewGrade.Easy, maxIntervalDays: 180));
        Assert.Equal(180, Sm2.PreviewDays(s, ReviewGrade.Hard, maxIntervalDays: 180));

        Sm2.Apply(s, ReviewGrade.Good, Now, maxIntervalDays: 180);
        Assert.Equal(180, s.IntervalDays);
        Assert.Equal(Now.AddDays(180), s.Due);
    }

    [Fact]
    public void MaxIntervalDays_DoesNotAffectAgain()
    {
        var s = new SrsState { IntervalDays = 200, EaseFactor = 2.5, Repetitions = 5 };
        Sm2.Apply(s, ReviewGrade.Again, Now, maxIntervalDays: 180);

        Assert.Equal(0, s.IntervalDays);
        Assert.Equal(Now.AddMinutes(10), s.Due);
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

    [Fact]
    public void Relearn_IsALapse_ThatKeepsEase_AndIsDueNow()
    {
        var reviewed = Now.AddDays(-20);
        var s = new SrsState
        {
            IntervalDays = 30, EaseFactor = 2.7, Repetitions = 4, Lapses = 1,
            Due = Now.AddDays(10), LastReviewed = reviewed,
        };

        Sm2.Relearn(s, Now);

        Assert.Equal(0, s.Repetitions);
        Assert.Equal(0, s.IntervalDays);
        Assert.Equal(2, s.Lapses);
        Assert.Equal(2.7, s.EaseFactor);
        Assert.Equal(Now, s.Due); // future Due pulled in to now
        Assert.Equal(reviewed, s.LastReviewed);
    }

    [Fact]
    public void Relearn_KeepsAnOverdueDue_SoTheCardKeepsItsPlace()
    {
        var overdue = Now.AddDays(-7);
        var s = new SrsState { IntervalDays = 30, Repetitions = 4, Due = overdue };

        Sm2.Relearn(s, Now);

        Assert.Equal(overdue, s.Due);
    }

    [Fact]
    public void Relearn_ThenPassingGrades_RestartTheLadder()
    {
        var relearned = new SrsState { IntervalDays = 30, EaseFactor = 2.5, Repetitions = 4, Due = Now };
        Sm2.Relearn(relearned, Now);

        Assert.Equal(1, Sm2.PreviewDays(relearned, ReviewGrade.Hard));
        Assert.Equal(1, Sm2.PreviewDays(relearned, ReviewGrade.Good));
        Assert.Equal(Sm2.EasyFirstIntervalDays, Sm2.PreviewDays(relearned, ReviewGrade.Easy));
    }
}
