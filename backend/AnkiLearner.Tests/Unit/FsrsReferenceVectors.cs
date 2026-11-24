using AnkiLearner.Core.Srs;

namespace AnkiLearner.Tests.Unit;

/// <summary>
/// Expected FSRS-6 values, generated from a calendar-day reference replay that matched Anki's own
/// <c>compute_memory_state</c> exactly on a real 1.4k-card collection, and that agrees with py-fsrs
/// 6.3.3 wherever calendar days equal 24-hour periods. Regenerate rather than hand-edit.
/// Each step: review time (UTC), grade, elapsed calendar days (null = first review), S and D after
/// the review, R at review time.
/// </summary>
public static class FsrsReferenceVectors
{
    public sealed record Step(string At, ReviewGrade Grade, int? Elapsed, double S, double D, double? R);

    public sealed record Vector(
        string Name, IReadOnlyList<double> Weights, Step[] Steps,
        int Interval90, int Interval85, int Interval85Cap30);

    public static readonly IReadOnlyList<double> DefaultWeights = Fsrs.DefaultWeights;

    /// <summary>A made-up in-bounds weight set (nobody's fitted weights) exercising every weight.</summary>
    public static readonly IReadOnlyList<double> CustomWeights =
        [0.5, 3.0, 5.0, 15.0, 6.0, 0.9, 2.5, 0.15, 2.0, 0.12, 1.0, 1.6, 0.05, 0.25, 1.8, 0.4, 2.2, 0.6, 0.15, 0.08, 0.2];

    public static readonly Vector[] All =
    [
        new("Default/FirstAgain", DefaultWeights,
        [
            new("2026-01-01T10:00", ReviewGrade.Again, null, 0.212, 6.4133, null),
        ],
        Interval90: 1, Interval85: 1, Interval85Cap30: 1),
        new("Default/FirstHard", DefaultWeights,
        [
            new("2026-01-01T10:00", ReviewGrade.Hard, null, 1.2931, 5.112170705601055, null),
        ],
        Interval90: 1, Interval85: 2, Interval85Cap30: 2),
        new("Default/FirstGood", DefaultWeights,
        [
            new("2026-01-01T10:00", ReviewGrade.Good, null, 2.3065, 2.118103970459015, null),
        ],
        Interval90: 2, Interval85: 4, Interval85Cap30: 4),
        new("Default/FirstEasy", DefaultWeights,
        [
            new("2026-01-01T10:00", ReviewGrade.Easy, null, 8.2956, 1.0, null),
        ],
        Interval90: 8, Interval85: 16, Interval85Cap30: 16),
        new("Default/MixedHistory", DefaultWeights,
        [
            new("2026-01-01T10:00", ReviewGrade.Good, null, 2.3065, 2.118103970459015, null),
            new("2026-01-04T10:00", ReviewGrade.Good, 3, 13.826903694354572, 2.1112142357853942, 0.8809479557659419),
            new("2026-01-14T18:00", ReviewGrade.Good, 10, 47.4520337284769, 2.1043313908464474, 0.920684082572877),
            new("2026-02-13T09:00", ReviewGrade.Again, 30, 2.829104453485895, 7.389975788014609, 0.9283278514976722),
            new("2026-02-13T09:10", ReviewGrade.Good, 0, 2.829104453485895, 7.377814181523433, 1.0),
            new("2026-02-15T22:00", ReviewGrade.Good, 2, 6.417644338737023, 7.365664736638748, 0.9220179348060746),
            new("2026-02-20T08:00", ReviewGrade.Easy, 5, 20.790033730679557, 6.470621020943528, 0.9162160435653447),
            new("2026-03-12T12:00", ReviewGrade.Hard, 20, 38.68876767477668, 7.642262133938728, 0.902639423565676),
        ],
        Interval90: 39, Interval85: 74, Interval85Cap30: 30),
        new("Default/SameDayLadder", DefaultWeights,
        [
            new("2026-01-01T10:00", ReviewGrade.Again, null, 0.212, 6.4133, null),
            new("2026-01-01T10:10", ReviewGrade.Again, 0, 0.08335671711031603, 8.806304468856837, 1.0),
            new("2026-01-01T10:20", ReviewGrade.Hard, 0, 0.08335671711031603, 9.192797649512254, 1.0),
            new("2026-01-01T10:30", ReviewGrade.Good, 0, 0.1031406500778523, 9.17883322115958, 1.0),
            new("2026-01-02T09:00", ReviewGrade.Good, 1, 0.5916793160261826, 9.164882757235258, 0.6958266544079361),
        ],
        Interval90: 1, Interval85: 1, Interval85Cap30: 1),
        new("Default/CrossMidnight", DefaultWeights,
        [
            new("2026-01-01T23:50", ReviewGrade.Good, null, 2.3065, 2.118103970459015, null),
            new("2026-01-02T00:10", ReviewGrade.Good, 1, 7.315300744077282, 2.1112142357853942, 0.9468474993825461),
            new("2026-01-02T00:20", ReviewGrade.Good, 0, 7.315300744077282, 2.1043313908464474, 1.0),
        ],
        Interval90: 7, Interval85: 14, Interval85Cap30: 14),
        new("Default/LongGap", DefaultWeights,
        [
            new("2025-05-10T20:00", ReviewGrade.Good, null, 2.3065, 2.118103970459015, null),
            new("2025-05-12T20:00", ReviewGrade.Good, 2, 10.964332335820703, 2.1112142357853942, 0.9094932559773545),
            new("2026-11-24T12:00", ReviewGrade.Good, 561, 196.51770928301724, 2.1043313908464474, 0.5451079258524426),
        ],
        Interval90: 197, Interval85: 375, Interval85Cap30: 30),
        new("Default/LapseAfterLongGap", DefaultWeights,
        [
            new("2025-05-10T20:00", ReviewGrade.Easy, null, 8.2956, 1.0, null),
            new("2025-06-10T20:00", ReviewGrade.Good, 31, 77.76768926342325, 1.0, 0.7886511485820531),
            new("2026-11-24T12:00", ReviewGrade.Again, 532, 4.982491979976432, 7.0269895692968385, 0.7298727418337179),
        ],
        Interval90: 5, Interval85: 9, Interval85Cap30: 9),
        new("Custom/FirstAgain", CustomWeights,
        [
            new("2026-01-01T10:00", ReviewGrade.Again, null, 0.5, 6.0, null),
        ],
        Interval90: 1, Interval85: 1, Interval85Cap30: 1),
        new("Custom/FirstHard", CustomWeights,
        [
            new("2026-01-01T10:00", ReviewGrade.Hard, null, 3.0, 4.54039688884305, null),
        ],
        Interval90: 3, Interval85: 5, Interval85Cap30: 5),
        new("Custom/FirstGood", CustomWeights,
        [
            new("2026-01-01T10:00", ReviewGrade.Good, null, 5.0, 1.0, null),
        ],
        Interval90: 5, Interval85: 9, Interval85Cap30: 9),
        new("Custom/FirstEasy", CustomWeights,
        [
            new("2026-01-01T10:00", ReviewGrade.Easy, null, 15.0, 1.0, null),
        ],
        Interval90: 15, Interval85: 27, Interval85Cap30: 27),
        new("Custom/MixedHistory", CustomWeights,
        [
            new("2026-01-01T10:00", ReviewGrade.Good, null, 5.0, 1.0, null),
            new("2026-01-04T10:00", ReviewGrade.Good, 3, 26.175592083907958, 1.0, 0.9327835795542339),
            new("2026-01-14T18:00", ReviewGrade.Good, 10, 87.59850245063123, 1.0, 0.9540819575920266),
            new("2026-02-13T09:00", ReviewGrade.Again, 30, 3.566888380018782, 3.9180402412690745, 0.9582756713125226),
            new("2026-02-13T09:10", ReviewGrade.Good, 0, 3.566888380018782, 2.1483744463477876, 1.0),
            new("2026-02-15T22:00", ReviewGrade.Good, 2, 16.714959917803462, 1.0, 0.9364149798535313),
            new("2026-02-20T08:00", ReviewGrade.Easy, 5, 89.7634645584288, 1.0, 0.962999494646944),
            new("2026-03-12T12:00", ReviewGrade.Hard, 20, 134.2015684828294, 1.7930402412690747, 0.971672184611175),
        ],
        Interval90: 134, Interval85: 243, Interval85Cap30: 30),
        new("Custom/SameDayLadder", CustomWeights,
        [
            new("2026-01-01T10:00", ReviewGrade.Again, null, 0.5, 6.0, null),
            new("2026-01-01T10:10", ReviewGrade.Again, 0, 0.17417488364257028, 5.806929130157963, 1.0),
            new("2026-01-01T10:20", ReviewGrade.Hard, 0, 0.17417488364257028, 4.74396062394938, 1.0),
            new("2026-01-01T10:30", ReviewGrade.Good, 0, 0.21917609760518708, 2.850406771626047, 1.0),
            new("2026-01-02T09:00", ReviewGrade.Good, 1, 4.680478543132094, 1.2408859971512147, 0.7517864035437656),
        ],
        Interval90: 5, Interval85: 8, Interval85Cap30: 8),
        new("Custom/CrossMidnight", CustomWeights,
        [
            new("2026-01-01T23:50", ReviewGrade.Good, null, 5.0, 1.0, null),
            new("2026-01-02T00:10", ReviewGrade.Good, 1, 12.91107950368402, 1.0, 0.974356758459812),
            new("2026-01-02T00:20", ReviewGrade.Good, 0, 12.91107950368402, 1.0, 1.0),
        ],
        Interval90: 13, Interval85: 23, Interval85Cap30: 23),
        new("Custom/LongGap", CustomWeights,
        [
            new("2025-05-10T20:00", ReviewGrade.Good, null, 5.0, 1.0, null),
            new("2025-05-12T20:00", ReviewGrade.Good, 2, 19.90750323028049, 1.0, 0.9522135973928125),
            new("2026-11-24T12:00", ReviewGrade.Good, 561, 609.6720951086677, 1.0, 0.5463436921146729),
        ],
        Interval90: 610, Interval85: 1102, Interval85Cap30: 30),
        new("Custom/LapseAfterLongGap", CustomWeights,
        [
            new("2025-05-10T20:00", ReviewGrade.Easy, null, 15.0, 1.0, null),
            new("2025-06-10T20:00", ReviewGrade.Good, 31, 156.71173273484064, 1.0, 0.8370715869389326),
            new("2026-11-24T12:00", ReviewGrade.Again, 532, 5.9931676441404935, 3.9180402412690745, 0.7850179368948479),
        ],
        Interval90: 6, Interval85: 11, Interval85Cap30: 11),
    ];
}
