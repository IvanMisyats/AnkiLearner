using System.IO.Compression;
using Microsoft.Data.Sqlite;
using ZstdSharp;

namespace AnkiLearner.Tests.Fixtures;

public record FixtureNote(long Id, string Front, string Back, string Tags = "");

public record FixtureCard(
    long NoteId, int Ord, int Type, int Queue, long Due, int Ivl, int Factor, int Reps, int Lapses);

/// <summary>
/// Builds a minimal but structurally real Anki v3 .apkg in memory: a ZIP containing a
/// zstd-compressed SQLite database (collection.anki21b) with the tables the importer reads.
/// </summary>
public static class ApkgFixture
{
    public const char FieldSeparator = '\x1f';

    public static byte[] Build(
        long collectionCreatedEpoch,
        IReadOnlyList<FixtureNote> notes,
        IReadOnlyList<FixtureCard> cards,
        string deckName = "TestDeck",
        long deckId = 1)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apkg-fixture-{Guid.NewGuid():N}.sqlite");
        try
        {
            CreateDatabase(dbPath, collectionCreatedEpoch, notes, cards, deckName, deckId);
            SqliteConnection.ClearAllPools(); // release the file lock before reading bytes
            var dbBytes = File.ReadAllBytes(dbPath);
            return Zip(Compress(dbBytes));
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    /// <summary>A ZIP without collection.anki21b — simulates a legacy (pre-v3) package.</summary>
    public static byte[] BuildLegacy()
    {
        using var zipStream = new MemoryStream();
        using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var entry = zip.CreateEntry("collection.anki2").Open();
            entry.Write("legacy"u8);
        }
        return zipStream.ToArray();
    }

    private static void CreateDatabase(
        string dbPath, long crt, IReadOnlyList<FixtureNote> notes, IReadOnlyList<FixtureCard> cards,
        string deckName, long deckId)
    {
        using var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();
        connection.CreateCollation("unicase", (a, b) =>
            string.Compare(a, b, StringComparison.OrdinalIgnoreCase));

        Execute(connection, """
            CREATE TABLE col (crt INTEGER);
            CREATE TABLE notes (id INTEGER PRIMARY KEY, flds TEXT, tags TEXT);
            CREATE TABLE cards (
                id INTEGER PRIMARY KEY, nid INTEGER, did INTEGER, ord INTEGER,
                type INTEGER, queue INTEGER, due INTEGER, ivl INTEGER,
                factor INTEGER, reps INTEGER, lapses INTEGER);
            CREATE TABLE decks (id INTEGER PRIMARY KEY, name TEXT COLLATE unicase);
            """);
        Execute(connection, $"INSERT INTO col (crt) VALUES ({crt})");
        Execute(connection, $"INSERT INTO decks (id, name) VALUES ({deckId}, '{deckName}')");

        foreach (var note in notes)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO notes (id, flds, tags) VALUES ($id, $flds, $tags)";
            command.Parameters.AddWithValue("$id", note.Id);
            command.Parameters.AddWithValue("$flds", $"{note.Front}{FieldSeparator}{note.Back}");
            command.Parameters.AddWithValue("$tags", note.Tags);
            command.ExecuteNonQuery();
        }

        var cardId = 1;
        foreach (var card in cards)
        {
            Execute(connection, $"""
                INSERT INTO cards (id, nid, did, ord, type, queue, due, ivl, factor, reps, lapses)
                VALUES ({cardId++}, {card.NoteId}, {deckId}, {card.Ord}, {card.Type},
                        {card.Queue}, {card.Due}, {card.Ivl}, {card.Factor}, {card.Reps}, {card.Lapses})
                """);
        }
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static byte[] Compress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zstd = new CompressionStream(output))
        {
            zstd.Write(data);
        }
        return output.ToArray();
    }

    private static byte[] Zip(byte[] compressedDb)
    {
        using var zipStream = new MemoryStream();
        using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var meta = zip.CreateEntry("meta").Open())
            {
                meta.Write([0x08, 0x03]); // protobuf: version 3
            }
            using var entry = zip.CreateEntry("collection.anki21b").Open();
            entry.Write(compressedDb);
        }
        return zipStream.ToArray();
    }
}
