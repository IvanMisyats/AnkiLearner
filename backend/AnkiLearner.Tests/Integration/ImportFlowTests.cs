using System.Net;
using System.Net.Http.Json;
using AnkiLearner.Api.Contracts;
using AnkiLearner.Core.Entities;
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

        // The SM-2 state carries interval/ease/lapses; due = crt + 120 d (in the past ⇒ due now).
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var state = await db.SrsStates.SingleAsync(s =>
            s.WordId == Guid.Parse(hund.Id.ToString()) && s.Exercise == ExerciseType.TargetToKnown);
        Assert.Equal(30, state.IntervalDays);
        Assert.Equal(2.5, state.EaseFactor, precision: 10);
        Assert.Equal(1, state.Lapses);
        Assert.Equal(12, state.Repetitions);
        Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(120), state.Due);

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
        HttpClient client, string importId, bool importProgress, bool importDuplicates = false) =>
        await (await client.PostAsJsonAsync($"/api/import/apkg/{importId}/commit",
            new { importDuplicates, importProgress })).ReadAsAsync<CommitDto>();

    private record PreviewDto(string ImportId, int Total, int New, int Duplicates, int WithProgress, List<string> Skipped);
    private record CommitDto(int Imported, int StatesImported);
    private record CountsDto(string Exercise, int Due, int New);
}
