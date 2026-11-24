using System.Net;
using System.Net.Http.Json;
using AnkiLearner.Api.Contracts;
using AnkiLearner.Core.Entities;
using AnkiLearner.Core.Srs;
using AnkiLearner.Infrastructure.Data;
using AnkiLearner.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AnkiLearner.Tests.Integration;

public class ImportFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly long Crt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
        .ToUnixTimeSeconds();

    [Fact]
    public async Task Upload_Preview_Commit_ImportsWordsTagsAndProgress()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var apkg = ApkgFixture.Build(
            Crt,
            notes:
            [
                new FixtureNote(1, "hund", "dog", Tags: "animals"),
                new FixtureNote(2, "kat", "cat"),
            ],
            cards:
            [
                // Mature review card, both directions.
                new FixtureCard(NoteId: 1, Ord: 0, Type: 2, Queue: 2, Due: 120, Ivl: 30, Factor: 2500, Reps: 12, Lapses: 1),
                new FixtureCard(NoteId: 1, Ord: 1, Type: 2, Queue: 2, Due: 90, Ivl: 15, Factor: 2100, Reps: 8, Lapses: 2),
                // Note 2 is still new in Anki — no progress to carry over.
                new FixtureCard(NoteId: 2, Ord: 0, Type: 0, Queue: 0, Due: 3, Ivl: 0, Factor: 0, Reps: 0, Lapses: 0),
            ],
            deckName: "Danish");

        var preview = await UploadAsync(client, apkg);
        Assert.Equal(2, preview.Total);
        Assert.Equal(2, preview.New);
        Assert.Equal(0, preview.Duplicates);
        Assert.Equal(1, preview.WithProgress);
        Assert.Empty(preview.Skipped);

        var commit = await CommitAsync(client, preview.ImportId, importProgress: true);
        Assert.Equal(2, commit.Imported);
        Assert.Equal(2, commit.StatesImported); // both directions of note 1

        // Words + tags landed correctly.
        var words = await (await client.GetAsync("/api/words?search=hund"))
            .ReadAsAsync<PagedResponse<WordDto>>();
        var hund = Assert.Single(words.Items);
        Assert.Equal("dog", hund.Translations.Single().Text);
        Assert.Equal("en", hund.Translations.Single().LanguageCode);
        Assert.Contains("imported", hund.Tags);
        Assert.Contains("Danish", hund.Tags);
        Assert.Contains("animals", hund.Tags);

        // The import maps Anki's interval/ease/lapses (SM-2), then FSRS setup gives the card a
        // memory from that snapshot (no history here) and reschedules it.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var state = await db.SrsStates.SingleAsync(s =>
            s.WordId == hund.Id && s.Exercise == ExerciseType.TargetToKnown);
        Assert.Equal(2.5, state.EaseFactor, precision: 10);
        Assert.Equal(1, state.Lapses);
        Assert.Equal(12, state.Repetitions);
        var ankiDue = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(120);
        Assert.Equal(30, state.Stability);
        Assert.Equal(FsrsSetup.HeuristicDifficulty(1, 12, Fsrs.DefaultWeights), state.Difficulty);
        Assert.Equal(ankiDue.AddDays(-30), state.LastReviewed); // last review = Anki due − interval
        Assert.Equal(state.LastReviewed!.Value.AddDays(state.IntervalDays), state.Due);

        // The setup is logged with the SM-2 values it started from.
        var setup = await db.ReviewLog.SingleAsync(r =>
            r.WordId == hund.Id && r.Exercise == ExerciseType.TargetToKnown);
        Assert.Equal(ReviewKind.Setup, setup.Kind);
        Assert.Equal(30, setup.IntervalBefore);
        Assert.Equal(ankiDue, setup.DueBefore);
        Assert.Null(setup.PreviousReviewAt);
        Assert.Equal(state.Stability, setup.StabilityAfter);

        // And the imported card shows up as due for study.
        var counts = await (await client.GetAsync("/api/study/counts"))
            .ReadAsAsync<List<CountsDto>>();
        Assert.Equal(1, counts.Single(c => c.Exercise == "TargetToKnown").Due);
    }

    [Fact]
    public async Task Commit_WithoutProgress_LeavesAllWordsNew()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var apkg = ApkgFixture.Build(Crt,
            notes: [new FixtureNote(1, "bord", "table")],
            cards: [new FixtureCard(1, 0, Type: 2, Queue: 2, Due: 10, Ivl: 5, Factor: 2500, Reps: 3, Lapses: 0)]);

        var preview = await UploadAsync(client, apkg);
        var commit = await CommitAsync(client, preview.ImportId, importProgress: false);

        Assert.Equal(1, commit.Imported);
        Assert.Equal(0, commit.StatesImported);
        var counts = await (await client.GetAsync("/api/study/counts"))
            .ReadAsAsync<List<CountsDto>>();
        Assert.Equal(1, counts.Single(c => c.Exercise == "TargetToKnown").New);
    }

    [Fact]
    public async Task Duplicates_AreSkippedByDefault_AndImportableOnRequest()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        await client.PostAsJsonAsync("/api/words", new
        {
            term = "hest",
            translations = new[] { new { languageCode = "en", text = "horse", exampleTranslation = (string?)null } },
        });

        var apkg = ApkgFixture.Build(Crt,
            notes: [new FixtureNote(1, "<b>Hest</b>", "horse (imported)")],
            cards: []);

        var preview = await UploadAsync(client, apkg);
        Assert.Equal(1, preview.Duplicates);
        Assert.Equal(0, preview.New);

        var skipping = await CommitAsync(client, preview.ImportId, importProgress: true);
        Assert.Equal(0, skipping.Imported);

        // Re-upload (commit consumes the staging entry) and import duplicates this time.
        var second = await UploadAsync(client, apkg);
        var importing = await CommitAsync(client, second.ImportId, importProgress: true, importDuplicates: true);
        Assert.Equal(1, importing.Imported);
    }

    [Fact]
    public async Task LegacyPackage_IsRejectedWithClearMessage()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var response = await PostFileAsync(client, ApkgFixture.BuildLegacy());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Re-export", body);
    }

    [Fact]
    public async Task Commit_OfAnotherUsersImport_Returns404()
    {
        var alice = await factory.CreateAuthenticatedClientAsync();
        var bob = await factory.CreateAuthenticatedClientAsync();
        var apkg = ApkgFixture.Build(Crt, notes: [new FixtureNote(1, "sol", "sun")], cards: []);

        var preview = await UploadAsync(alice, apkg);
        var response = await bob.PostAsJsonAsync($"/api/import/apkg/{preview.ImportId}/commit",
            new { importDuplicates = false, importProgress = true });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --- review history (FR-I9) ---

    private static readonly DateTime T0 = new(2026, 1, 10, 19, 0, 0, DateTimeKind.Utc);

    /// <summary>"hund" (forward card with 3 reviews) and "kat" (no history); crt 2026-01-01.</summary>
    private static byte[] DeckWithHistory(string? extraNote = null) => ApkgFixture.Build(
        Crt,
        notes: extraNote is null
            ? [new FixtureNote(1, "hund", "dog"), new FixtureNote(2, "kat", "cat")]
            : [new FixtureNote(1, "hund", "dog"), new FixtureNote(2, "kat", "cat"), new FixtureNote(3, extraNote, "x")],
        cards:
        [
            new FixtureCard(NoteId: 1, Ord: 0, Type: 2, Queue: 2, Due: 40, Ivl: 20, Factor: 2500, Reps: 3, Lapses: 0),
            new FixtureCard(NoteId: 2, Ord: 0, Type: 2, Queue: 2, Due: 30, Ivl: 5, Factor: 2500, Reps: 2, Lapses: 0),
        ],
        reviews:
        [
            new FixtureReview(0, T0, Ease: 3, Ivl: 1, LastIvl: -600, Type: 0),
            new FixtureReview(0, T0.AddDays(1), Ease: 3, Ivl: 6, LastIvl: 1),
            new FixtureReview(0, T0.AddDays(7), Ease: 4, Ivl: 20, LastIvl: 6),
        ]);

    private static readonly (DateTime, ReviewGrade)[] HundHistory =
        [(T0, ReviewGrade.Good), (T0.AddDays(1), ReviewGrade.Good), (T0.AddDays(7), ReviewGrade.Easy)];

    [Fact]
    public async Task Import_WithHistory_StoresTheRevlog_AndStartsFsrsFromIt()
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        var preview = await UploadAsync(client, DeckWithHistory());
        Assert.Equal(0, preview.ExistingWithHistory); // nothing in the dictionary yet
        var commit = await CommitAsync(client, preview.ImportId, importProgress: true, importHistory: true);

        Assert.Equal(2, commit.Imported);
        Assert.Equal(1, commit.HistoryCards);
        Assert.Equal(3, commit.HistoryReviews);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hund = await WordIdAsync(db, client, "hund");
        var rows = await db.ReviewLog.Where(r => r.WordId == hund && r.Source == ReviewSource.Anki)
            .OrderBy(r => r.ReviewedAt).ToListAsync();
        Assert.Equal([ReviewGrade.Good, ReviewGrade.Good, ReviewGrade.Easy], rows.Select(r => r.Grade!.Value));
        Assert.Null(rows[0].IntervalBefore); // learning: negative lastIvl is seconds, not days
        Assert.Equal(6, rows[2].IntervalBefore);
        Assert.Equal(20, rows[2].IntervalAfter);
        Assert.Equal(rows[1].ReviewedAt, rows[2].PreviousReviewAt);
        Assert.Equal(new DateTimeOffset(T0).ToUnixTimeMilliseconds(), rows[0].AnkiRevlogId);

        var expected = Fsrs.Replay(HundHistory, Fsrs.DefaultWeights)!.Value;
        var state = await db.SrsStates.SingleAsync(s => s.WordId == hund);
        Assert.Equal(expected.Memory.Stability, state.Stability!.Value, 1e-9);
        Assert.Equal(expected.Memory.Difficulty, state.Difficulty!.Value, 1e-9);
    }

    [Fact]
    public async Task HistoryOnly_AddsNoWords_AttachesHistory_AndIsIdempotent()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        // First import without history, as before this feature existed.
        var first = await UploadAsync(client, DeckWithHistory());
        await CommitAsync(client, first.ImportId, importProgress: true);

        // The re-upload also carries a note the user deleted (or never imported): it must stay out.
        var preview = await UploadAsync(client, DeckWithHistory(extraNote: "slettet"));
        Assert.Equal(1, preview.ExistingWithHistory);
        var commit = await CommitAsync(client, preview.ImportId, importProgress: true, historyOnly: true);

        Assert.Equal(0, commit.Imported);
        Assert.Equal(1, commit.HistoryCards);
        var words = await (await client.GetAsync("/api/words?search=slettet")).ReadAsAsync<PagedResponse<WordDto>>();
        Assert.Empty(words.Items);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hund = await WordIdAsync(db, client, "hund");
        var expected = Fsrs.Replay(HundHistory, Fsrs.DefaultWeights)!.Value;
        var state = await db.SrsStates.AsNoTracking().SingleAsync(s => s.WordId == hund);
        Assert.Equal(expected.Memory.Stability, state.Stability!.Value, 1e-9); // set up again from the history

        // Uploading the same file again changes nothing, and the preview already says so.
        var again = await UploadAsync(client, DeckWithHistory());
        Assert.Equal(0, again.ExistingWithHistory);
        var second = await CommitAsync(client, again.ImportId, importProgress: true, historyOnly: true);
        Assert.Equal(0, second.HistoryCards);
        Assert.Equal(3, await db.ReviewLog.CountAsync(r => r.WordId == hund && r.Source == ReviewSource.Anki));
    }

    [Fact]
    public async Task HistoryOnly_WithTwoNotesForOneWord_TakesTheCardTheImportCameFrom()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        // Two "hund" notes: the first import kept only the first one (ease 2.5, 3 reps).
        var apkg = ApkgFixture.Build(
            Crt,
            notes: [new FixtureNote(1, "hund", "dog"), new FixtureNote(2, "hund", "hound")],
            cards:
            [
                new FixtureCard(NoteId: 1, Ord: 0, Type: 2, Queue: 2, Due: 40, Ivl: 20, Factor: 2500, Reps: 3, Lapses: 0),
                new FixtureCard(NoteId: 2, Ord: 0, Type: 2, Queue: 2, Due: 40, Ivl: 9, Factor: 2100, Reps: 5, Lapses: 1),
            ],
            reviews:
            [
                new FixtureReview(0, T0, Ease: 3), new FixtureReview(0, T0.AddDays(1), Ease: 3),
                new FixtureReview(1, T0.AddHours(1), Ease: 1), new FixtureReview(1, T0.AddDays(2), Ease: 2),
            ]);
        var first = await UploadAsync(client, apkg);
        await CommitAsync(client, first.ImportId, importProgress: true);

        var again = await UploadAsync(client, apkg);
        var commit = await CommitAsync(client, again.ImportId, importProgress: true, historyOnly: true);

        Assert.Equal(1, commit.HistoryCards);
        Assert.Equal(2, commit.HistoryReviews);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hund = await WordIdAsync(db, client, "hund");
        var grades = await db.ReviewLog.Where(r => r.WordId == hund && r.Source == ReviewSource.Anki)
            .OrderBy(r => r.ReviewedAt).Select(r => r.Grade).ToListAsync();
        Assert.Equal([ReviewGrade.Good, ReviewGrade.Good], grades); // note 1's card, not note 2's
    }

    [Fact]
    public async Task HistoryOnly_WhenTheImportedNoteHasNoReviews_DoesNotBorrowAnotherNotesHistory()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        // The first import kept note 1 (no revlog); note 2 shares the term and has history.
        var apkg = ApkgFixture.Build(
            Crt,
            notes: [new FixtureNote(1, "hund", "dog"), new FixtureNote(2, "hund", "hound")],
            cards:
            [
                new FixtureCard(NoteId: 1, Ord: 0, Type: 2, Queue: 2, Due: 40, Ivl: 20, Factor: 2500, Reps: 3, Lapses: 0),
                new FixtureCard(NoteId: 2, Ord: 0, Type: 2, Queue: 2, Due: 40, Ivl: 9, Factor: 2100, Reps: 5, Lapses: 1),
            ],
            reviews: [new FixtureReview(1, T0, Ease: 1), new FixtureReview(1, T0.AddDays(2), Ease: 2)]);
        var first = await UploadAsync(client, apkg);
        await CommitAsync(client, first.ImportId, importProgress: true);

        var again = await UploadAsync(client, apkg);
        Assert.Equal(0, again.ExistingWithHistory);
        var commit = await CommitAsync(client, again.ImportId, importProgress: true, historyOnly: true);

        Assert.Equal(0, commit.HistoryCards);
    }

    [Fact]
    public async Task Commit_Twice_TheSecondFindsTheUploadGone()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var preview = await UploadAsync(client, DeckWithHistory());
        await CommitAsync(client, preview.ImportId, importProgress: true);

        var second = await client.PostAsJsonAsync($"/api/import/apkg/{preview.ImportId}/commit",
            new { importDuplicates = true, importProgress = true });

        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode); // no second copy of every word
    }

    private static async Task<Guid> WordIdAsync(AppDbContext db, HttpClient client, string term)
    {
        var words = await (await client.GetAsync($"/api/words?search={term}")).ReadAsAsync<PagedResponse<WordDto>>();
        return words.Items.Single(w => w.Term == term).Id;
    }

    // --- helpers ---

    private static async Task<HttpResponseMessage> PostFileAsync(HttpClient client, byte[] bytes)
    {
        using var content = new MultipartFormDataContent
        {
            { new ByteArrayContent(bytes), "file", "test.apkg" },
        };
        return await client.PostAsync("/api/import/apkg", content);
    }

    private static async Task<PreviewDto> UploadAsync(HttpClient client, byte[] bytes) =>
        await (await PostFileAsync(client, bytes)).ReadAsAsync<PreviewDto>();

    private static async Task<CommitDto> CommitAsync(
        HttpClient client, string importId, bool importProgress, bool importDuplicates = false,
        bool importHistory = false, bool historyOnly = false) =>
        await (await client.PostAsJsonAsync($"/api/import/apkg/{importId}/commit",
            new { importDuplicates, importProgress, importHistory, historyOnly })).ReadAsAsync<CommitDto>();

    private record PreviewDto(
        string ImportId, int Total, int New, int Duplicates, int WithProgress, int ExistingWithHistory, List<string> Skipped);
    private record CommitDto(int Imported, int StatesImported, int HistoryCards, int HistoryReviews);
    private record CountsDto(string Exercise, int Due, int New);
}
