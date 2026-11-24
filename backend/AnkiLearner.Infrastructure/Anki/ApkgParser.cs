using System.IO.Compression;
using Microsoft.Data.Sqlite;
using ZstdSharp;

namespace AnkiLearner.Infrastructure.Anki;

/// <summary>
/// Reads a modern Anki v3 <c>.apkg</c> export (spec §3.5). Format notes (from the
/// DanishLearner POC research): the package is a ZIP whose real database is
/// <c>collection.anki21b</c>, a zstd-compressed SQLite file. The <c>decks</c> table
/// uses a custom <c>unicase</c> collation that must be registered on the connection,
/// and note fields in <c>notes.flds</c> are separated by the byte 0x1f.
/// </summary>
public static class ApkgParser
{
    private const char FieldSeparator = '\x1f';

    /// <summary>Cap on the decompressed database size — guards against zip bombs.</summary>
    private const long MaxDecompressedBytes = 256L * 1024 * 1024;

    public static ApkgParseResult Parse(Stream apkgStream)
    {
        using var zip = new ZipArchive(apkgStream, ZipArchiveMode.Read, leaveOpen: true);

        var dbEntry = zip.GetEntry("collection.anki21b")
            ?? throw new ApkgFormatException(
                "This file is not a modern Anki package (missing collection.anki21b). " +
                "Re-export it from a current version of Anki or AnkiDroid and try again.");

        // zstd-decompress the database into a temp file so SQLite can open it.
        var tempDbPath = Path.Combine(Path.GetTempPath(), $"ankilearner-import-{Guid.NewGuid():N}.sqlite");
        try
        {
            using (var entryStream = dbEntry.Open())
            using (var zstd = new DecompressionStream(entryStream))
            using (var file = File.Create(tempDbPath))
            {
                CopyWithLimit(zstd, file, MaxDecompressedBytes);
            }
            return ReadDatabase(tempDbPath);
        }
        finally
        {
            TryDelete(tempDbPath);
        }
    }

    private static ApkgParseResult ReadDatabase(string dbPath)
    {
        using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        connection.Open();
        // The v3 schema declares deck names with COLLATE unicase (not built into SQLite).
        connection.CreateCollation("unicase", (a, b) =>
            string.Compare(a, b, StringComparison.OrdinalIgnoreCase));

        var created = DateTimeOffset.FromUnixTimeSeconds(QueryLong(connection, "SELECT crt FROM col LIMIT 1"))
            .UtcDateTime;
        var decks = ReadDecks(connection);
        var skipped = new List<string>();
        var reviewsByCard = ReadReviews(connection, skipped);
        var cardsByNote = ReadCards(connection, reviewsByCard);

        var notes = new List<ApkgNote>();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, flds, tags FROM notes";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var noteId = reader.GetInt64(0);
            var fields = reader.GetString(1).Split(FieldSeparator);
            var tags = reader.GetString(2)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

            if (fields.Length < 2 || string.IsNullOrWhiteSpace(fields[0]))
            {
                skipped.Add($"Note {noteId}: fewer than two fields or an empty front side.");
                continue;
            }

            var cards = cardsByNote.GetValueOrDefault(noteId, []);
            var deckName = cards.Count > 0 && decks.TryGetValue(cards[0].DeckId, out var name)
                ? name
                : string.Empty;

            notes.Add(new ApkgNote(
                fields[0],
                fields[1],
                tags,
                deckName,
                cards.Select(c => c.Card).ToList()));
        }

        return new ApkgParseResult(notes, skipped, created);
    }

    private static Dictionary<long, string> ReadDecks(SqliteConnection connection)
    {
        var decks = new Dictionary<long, string>();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, name FROM decks";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            // Nested deck names use the same 0x1f separator; join them Anki-style.
            var name = reader.GetString(1).Replace(FieldSeparator.ToString(), "::");
            decks[reader.GetInt64(0)] = name;
        }
        return decks;
    }

    private static Dictionary<long, List<(long DeckId, ApkgCard Card)>> ReadCards(
        SqliteConnection connection, Dictionary<long, List<ApkgReview>> reviewsByCard)
    {
        var byNote = new Dictionary<long, List<(long, ApkgCard)>>();
        var withReviews = new HashSet<long>(); // a crafted file may repeat a card id
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT nid, did, ord, type, queue, due, ivl, factor, reps, lapses, id FROM cards";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var noteId = reader.GetInt64(0);
            var card = new ApkgCard(
                Ord: reader.GetInt32(2),
                Type: reader.GetInt32(3),
                Queue: reader.GetInt32(4),
                Due: reader.GetInt64(5),
                IntervalDays: reader.GetInt32(6),
                EaseFactor: reader.GetInt32(7),
                Reps: reader.GetInt32(8),
                Lapses: reader.GetInt32(9))
            {
                Reviews = withReviews.Add(reader.GetInt64(10))
                    ? reviewsByCard.GetValueOrDefault(reader.GetInt64(10), [])
                    : [],
            };
            if (!byNote.TryGetValue(noteId, out var list))
                byNote[noteId] = list = [];
            list.Add((reader.GetInt64(1), card));
        }
        return byNote;
    }

    /// <summary>Cap on imported review history — guards memory (a parsed upload is staged for 30 minutes).</summary>
    private const long MaxReviews = 500_000;

    /// <summary>No real review predates Anki (2006); later than tomorrow is not a real review either.</summary>
    private static readonly long FirstPossibleReviewMs =
        new DateTimeOffset(2006, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

    /// <summary>
    /// Graded reviews per card id, oldest first (spec FR-I9), as Anki's own FSRS reads them: rows
    /// with a button press (ease 1–4) of types learning, review, relearning and filtered deck;
    /// an Anki "Forget" (a manual row with factor 0) wipes the card's earlier reviews; a
    /// filtered-deck row that didn't reschedule (factor 0) is skipped. The file is untrusted:
    /// impossible dates and duplicate ids are dropped, and a revlog that is too big or can't be
    /// read is left out (with a note) instead of failing the whole import. A package exported
    /// without scheduling has no `revlog` table at all.
    /// </summary>
    private static Dictionary<long, List<ApkgReview>> ReadReviews(SqliteConnection connection, List<string> skipped)
    {
        var byCard = new Dictionary<long, List<ApkgReview>>();
        if (QueryLong(connection, "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name = 'revlog'") == 0)
            return byCard;
        try
        {
            var total = QueryLong(connection, "SELECT count(*) FROM revlog");
            if (total > MaxReviews)
            {
                skipped.Add($"Review history: {total} reviews is over the limit of {MaxReviews}, so it was left out.");
                return byCard;
            }

            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT id, cid, ease, ivl, lastIvl, factor, type FROM revlog
                WHERE id BETWEEN $first AND $last
                ORDER BY id
                """;
            command.Parameters.AddWithValue("$first", FirstPossibleReviewMs);
            command.Parameters.AddWithValue("$last", DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeMilliseconds());
            using var reader = command.ExecuteReader();
            var seen = new HashSet<long>();
            while (reader.Read())
            {
                var id = reader.GetInt64(0);
                if (!seen.Add(id)) continue; // ids are unique in a real revlog
                var cardId = reader.GetInt64(1);
                var ease = reader.GetInt32(2);
                var factor = reader.GetInt32(5);
                var type = reader.GetInt32(6);

                // "Forget"/"Reset" (Anki 26: type 4, ease 0, factor 0 — its FSRS then treats the
                // card as new). "Set due date" is type 4 too, but keeps the factor: not a reset.
                if (type == 4 && factor == 0)
                {
                    if (byCard.TryGetValue(cardId, out var wiped)) wiped.Clear();
                    continue;
                }
                if (ease is < 1 or > 4 || type is < 0 or > 3) continue;
                if (type == 3 && factor == 0) continue; // filtered deck without rescheduling

                if (!byCard.TryGetValue(cardId, out var list))
                    byCard[cardId] = list = [];
                list.Add(new ApkgReview(
                    Id: id,
                    Ease: ease,
                    IntervalDays: reader.GetInt32(3),
                    LastIntervalDays: reader.GetInt32(4),
                    Factor: factor,
                    Type: type));
            }
        }
        catch (Exception e) when (e is SqliteException or InvalidCastException or InvalidOperationException
                                       or OverflowException or FormatException)
        {
            skipped.Add("Review history could not be read, so it was left out.");
            byCard.Clear();
        }
        return byCard;
    }

    private static long QueryLong(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar() is long value ? value : 0;
    }

    private static void CopyWithLimit(Stream source, Stream destination, long maxBytes)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (total > maxBytes)
                throw new ApkgFormatException("The package decompresses to an unreasonably large database.");
            destination.Write(buffer, 0, read);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Best effort — the OS temp cleaner will get it eventually.
        }
    }
}
