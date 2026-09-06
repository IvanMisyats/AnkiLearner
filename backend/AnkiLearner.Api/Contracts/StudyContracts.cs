using AnkiLearner.Core.Entities;
using AnkiLearner.Core.Srs;

namespace AnkiLearner.Api.Contracts;

/// <summary>Human-readable interval hints for the four grade buttons (e.g. "10 min", "6 d").</summary>
public record StudyIntervalsDto(string Again, string Hard, string Good, string Easy);

public record StudyCardDto(
    WordDto Word,
    string Prompt,
    string Answer,
    bool IsNew,
    StudyIntervalsDto Intervals);

/// <summary>Card is null when there is nothing left to study right now.</summary>
public record StudyNextResponse(StudyCardDto? Card, int Remaining);

/// <summary>Dashboard counters for one direction. <c>ReviewedToday</c> counts distinct words
/// reviewed since 00:00 UTC — the same day boundary the daily new-word limit uses.</summary>
public record StudyCountsDto(ExerciseType Exercise, int Due, int New, int ReviewedToday);

public record GradeRequest(Guid WordId, ExerciseType Exercise, ReviewGrade Grade);
