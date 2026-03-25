using System.Text;
using MediaEngine.Ingestion.Contracts;
using MediaEngine.Ingestion.Models;
using MediaEngine.Storage.Contracts;
using Microsoft.Extensions.Options;

namespace MediaEngine.Api.DevSupport;

/// <summary>
/// Development-only endpoints for seeding the library with test data.
/// Registered conditionally when <c>ASPNETCORE_ENVIRONMENT == "Development"</c>.
///
/// Endpoints:
///   POST /dev/seed-library  — Drop 37 test files (EPUBs + MP3s) into the Watch Folder
///   POST /dev/wipe           — Wipe DB, library root, watch folder, and reinitialize
///   POST /dev/full-test      — Wipe → Seed → return summary
/// </summary>
public static class DevSeedEndpoints
{
    /// <summary>A seed EPUB definition.</summary>
    private sealed record SeedBook(
        string Title,
        string Author,
        string Isbn,
        int Year,
        string Description,
        string? Publisher = null,
        string Language = "en",
        string[]? AdditionalAuthors = null,
        string? Series = null,
        int? SeriesPosition = null,
        string? TestCategory = null);

    /// <summary>A seed MP3 audiobook definition.</summary>
    private sealed record SeedAudiobook(
        string Title,
        string Artist,
        string Narrator,
        int Year,
        string Language = "eng",
        string? Series = null,
        int? SeriesPosition = null,
        string? Asin = null,
        string? TestCategory = null);

    // ── EPUB Seed definitions ────────────────────────────────────────────────
    // Real ISBNs so the hydration pipeline can fetch real cover art and metadata.

    private static readonly SeedBook[] SeedBooks =
    [
        // ── Category 1: Standard Cases (clean metadata, strong Wikidata presence) ──

        new("Dune",
            "Frank Herbert",
            "9780441013593", 1965,
            "Set on the desert planet Arrakis, Dune is the story of the boy Paul Atreides, heir to a noble family tasked with ruling an inhospitable world where the only thing of value is a spice capable of extending life and expanding consciousness.",
            TestCategory: "Standard"),

        new("Project Hail Mary",
            "Andy Weir",
            "9780593135204", 2021,
            "Ryland Grace is the sole survivor on a desperate, last-chance mission. If he fails, humanity and the earth itself will perish.",
            TestCategory: "Standard"),

        new("The Hobbit",
            "J.R.R. Tolkien",
            "9780547928227", 1937,
            "Bilbo Baggins is a hobbit who enjoys a comfortable, unambitious life, rarely travelling further than the pantry of his hobbit-hole in Bag End.",
            TestCategory: "Standard"),

        // ── Category 2: Pen Names ──────────────────────────────────────────

        new("Leviathan Wakes",
            "James S. A. Corey",
            "9780316129084", 2011,
            "Humanity has colonized the solar system. Jim Holden is XO of an ice hauler that makes a horrifying discovery in the asteroid belt.",
            Series: "The Expanse", SeriesPosition: 1,
            TestCategory: "PenName — collaborative (Daniel Abraham + Ty Franck)"),

        new("Caliban's War",
            "James S. A. Corey",
            "9780316129060", 2012,
            "On Ganymede, breadbasket of the outer planets, a Martian marine watches as her platoon is slaughtered by a monstrous supersoldier.",
            Series: "The Expanse", SeriesPosition: 2,
            TestCategory: "PenName — same collaborative pen name, series book #2"),

        new("The Shining",
            "Stephen King",
            "9780307743657", 1977,
            "Jack Torrance's new job at the Overlook Hotel is the perfect chance for a fresh start. But as the harsh winter weather sets in, the idyllic location feels ever more sinister.",
            TestCategory: "PenName — author also writes as Richard Bachman"),

        new("The Long Walk",
            "Richard Bachman",
            "9781501143823", 1979,
            "On the first day of May, one hundred teenage boys meet for an annual walking contest called The Long Walk.",
            TestCategory: "PenName — Stephen King writing as Richard Bachman"),

        // ── Category 3: Foreign Language ───────────────────────────────────

        new("Le Petit Prince",
            "Antoine de Saint-Exupéry",
            "9782070612758", 1943,
            "Un pilote, forcé d'atterrir dans le Sahara, rencontre un petit garçon venu d'une autre planète.",
            Language: "fr",
            TestCategory: "Foreign — French, accented author name"),

        new("Cien años de soledad",
            "Gabriel García Márquez",
            "9780307474728", 1967,
            "La historia de la familia Buendía a lo largo de siete generaciones en el pueblo ficticio de Macondo.",
            Language: "es",
            TestCategory: "Foreign — Spanish, special chars in title AND author"),

        new("Die Verwandlung",
            "Franz Kafka",
            "9783150091319", 1915,
            "Als Gregor Samsa eines Morgens aus unruhigen Träumen erwachte, fand er sich in seinem Bett zu einem ungeheueren Ungeziefer verwandelt.",
            Language: "de",
            TestCategory: "Foreign — German"),

        new("ノルウェイの森",
            "村上春樹",
            "9784062748681", 1987,
            "ワタナベトオルが、亡き親友キズキの恋人であった直子との関係を中心に、1960年代後半の東京での大学生活を回想する。",
            Language: "ja",
            TestCategory: "Foreign — Japanese, CJK title and author"),

        // ── Category 4: Series Books (Hub grouping + sequence) ─────────────

        new("Harry Potter and the Philosopher's Stone",
            "J.K. Rowling",
            "9780747532699", 1997,
            "Harry Potter has never even heard of Hogwarts when the letters start dropping on the doormat at number four, Privet Drive.",
            Series: "Harry Potter", SeriesPosition: 1,
            TestCategory: "Series — position 1"),

        new("Harry Potter and the Chamber of Secrets",
            "J.K. Rowling",
            "9780747538486", 1998,
            "Harry Potter's summer has included the worst birthday ever, doomy warnings from a house-elf called Dobby, and rescue from the Dursleys by his friend Ron Weasley in a magical flying car!",
            Series: "Harry Potter", SeriesPosition: 2,
            TestCategory: "Series — position 2, same author/series as above"),

        new("The Fellowship of the Ring",
            "J.R.R. Tolkien",
            "9780547928210", 1954,
            "In ancient times the Rings of Power were crafted by the Elven-smiths, and Sauron, the Dark Lord, forged the One Ring, filling it with his own power so that he could rule all others.",
            Series: "The Lord of the Rings", SeriesPosition: 1,
            TestCategory: "Series — same author as The Hobbit, different series"),

        // ── Category 5: Multiple Authors (co-authored, not pen name) ───────

        new("Good Omens",
            "Terry Pratchett",
            "9780060853983", 1990,
            "According to The Nice and Accurate Prophecies of Agnes Nutter, Witch, the world will end on a Saturday. Next Saturday, in fact.",
            AdditionalAuthors: ["Neil Gaiman"],
            TestCategory: "MultiAuthor — two distinct real authors"),

        new("The Talisman",
            "Stephen King",
            "9781501192272", 1984,
            "Jack Sawyer, twelve years old, is about to begin a most fantastic journey, an exhilarating, terrifying quest across the country and into another realm.",
            AdditionalAuthors: ["Peter Straub"],
            TestCategory: "MultiAuthor — King again (cross-ref with pen name tests)"),

        // ── Category 6: Edge Cases ─────────────────────────────────────────

        new("Untitled Book",
            "Unknown",
            "", 0,
            "",
            TestCategory: "Edge — minimal metadata: no ISBN, no year, no description"),

        new("A",
            "B",
            "9780140449136", 2000,
            "A very short title.",
            TestCategory: "Edge — extremely short title and author"),

        new("The Extraordinary & Fantastical Adventures of Dr. Enid Hartwell-Smythe III: A Most Peculiar Chronicle",
            "Reginald Fortescue-Pemberton IV",
            "9780000000001", 2020,
            "A book with an extraordinarily long title and author name designed to test truncation, file naming, and display in constrained UI elements.",
            TestCategory: "Edge — very long title and author, special chars (& : .)"),

        new("1Q84",
            "Haruki Murakami",
            "9780307593313", 2009,
            "A young woman named Aomame follows a taxi driver's suggestion and climbs down an emergency stairway into a world she calls 1Q84.",
            TestCategory: "Edge — numeric-starting title, same author as Japanese entry"),

        new("The Road",
            "Cormac McCarthy",
            "9780307387899", 2006,
            "A father and his son walk alone through burned America, heading through the ravaged landscape to the coast.",
            TestCategory: "Edge — standalone, no series (single-work Hub)"),

        // ── Category 7: Publisher Metadata ──────────────────────────────────

        new("Frankenstein",
            "Mary Shelley",
            "9780141439471", 1818,
            "Obsessed with creating life itself, Victor Frankenstein plunders graveyards for the material to fashion a new being.",
            Publisher: "Lackington, Hughes, Harding, Mavor & Jones",
            TestCategory: "Publisher — very old book, long publisher name with special chars"),

        // ── Category 8: Standalone classics (audiobook pairing targets) ─────

        new("Neuromancer",
            "William Gibson",
            "9780441569595", 1984,
            "The sky above the port was the color of television, tuned to a dead channel.",
            TestCategory: "Standalone — cyberpunk classic, audiobook pair target"),
    ];

    // ── MP3 Audiobook Seed definitions ───────────────────────────────────────
    // Paired with EPUBs above to test cross-format Hub grouping and Stage 2
    // bridge resolution. Genre tag set to "Audiobook" for disambiguation.

    private static readonly SeedAudiobook[] SeedAudiobooks =
    [
        // ── Paired with EPUB counterparts ─────────────────────────────────────

        new("Dune", "Frank Herbert", "Simon Vance", 1965,
            Series: "Dune Chronicles", SeriesPosition: 1,
            TestCategory: "Audiobook pair — Dune (Simon Vance narrator)"),

        new("Project Hail Mary", "Andy Weir", "Ray Porter", 2021,
            TestCategory: "Audiobook pair — standalone, popular narrator"),

        new("The Hobbit", "J.R.R. Tolkien", "Andy Serkis", 1937,
            TestCategory: "Audiobook pair — celebrity narrator"),

        new("Good Omens", "Terry Pratchett and Neil Gaiman", "Martin Jarvis", 1990,
            TestCategory: "Audiobook pair — multi-author work"),

        new("1Q84", "Haruki Murakami", "Allison Hiroto", 2009,
            TestCategory: "Audiobook pair — numeric-starting title"),

        new("The Shining", "Stephen King", "Campbell Scott", 1977,
            TestCategory: "Audiobook pair — pen name author (King/Bachman)"),

        new("Le Petit Prince", "Antoine de Saint-Exupery", "Bernard Giraudeau", 1943,
            Language: "fra",
            TestCategory: "Audiobook pair — foreign language (French)"),

        new("Harry Potter and the Philosopher's Stone", "J.K. Rowling", "Stephen Fry", 1997,
            Series: "Harry Potter", SeriesPosition: 1,
            TestCategory: "Audiobook pair — series book with famous narrator"),

        new("The Name of the Wind", "Patrick Rothfuss", "Nick Podehl", 2007,
            Series: "The Kingkiller Chronicle", SeriesPosition: 1,
            TestCategory: "Audiobook pair — series (no EPUB counterpart in series list)"),

        new("Leviathan Wakes", "James S. A. Corey", "Jefferson Mays", 2011,
            Series: "The Expanse", SeriesPosition: 1,
            TestCategory: "Audiobook pair — pen name series"),

        new("Foundation", "Isaac Asimov", "Scott Brick", 1951,
            Series: "Foundation", SeriesPosition: 1,
            TestCategory: "Audiobook pair — classic series (no EPUB counterpart)"),

        new("The Fellowship of the Ring", "J.R.R. Tolkien", "Rob Inglis", 1954,
            Series: "The Lord of the Rings", SeriesPosition: 1,
            TestCategory: "Audiobook pair — classic series with iconic narrator"),

        new("Neuromancer", "William Gibson", "Robertson Dean", 1984,
            TestCategory: "Audiobook pair — standalone classic"),

        new("The Road", "Cormac McCarthy", "Tom Stechschulte", 2006,
            TestCategory: "Audiobook pair — standalone"),

        // ── Multiple editions test (same work, different narrator) ──────────

        new("Dune", "Frank Herbert", "Scott Brick", 1965,
            Series: "Dune Chronicles", SeriesPosition: 1,
            TestCategory: "Multiple editions — Dune with alternate narrator"),
    ];

    // ── Endpoint registration ────────────────────────────────────────────────

    public static void MapDevSeedEndpoints(this WebApplication app)
    {
        RouteGroupBuilder group = app.MapGroup("/dev")
            .WithTags("Development");

        group.MapPost("/seed-library", SeedLibraryAsync)
            .WithSummary($"Drop {SeedBooks.Length} EPUBs + {SeedAudiobooks.Length} MP3 audiobooks into the Watch Folder");

        group.MapPost("/wipe", WipeAsync)
            .WithSummary("Wipe database, library root, and watch folder — then reinitialize a fresh DB");

        group.MapPost("/full-test", FullTestAsync)
            .WithSummary("Wipe everything → seed 37 test files → return summary");
    }

    // ── POST /dev/seed-library ───────────────────────────────────────────────

    private static async Task<IResult> SeedLibraryAsync(
        IOptions<IngestionOptions> options,
        ILogger<Program> logger)
    {
        string? watchDir = options.Value.WatchDirectory;

        if (string.IsNullOrWhiteSpace(watchDir))
        {
            return Results.BadRequest(new
            {
                error = "Watch Folder is not configured. Set it via PUT /settings/folders first."
            });
        }

        if (!Directory.Exists(watchDir))
        {
            try
            {
                Directory.CreateDirectory(watchDir);
                logger.LogInformation("Created Watch Folder at {Path}", watchDir);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new
                {
                    error = $"Cannot create Watch Folder: {ex.Message}"
                });
            }
        }

        var created = new List<string>();
        int skipped = 0;

        // ── Seed EPUBs ──────────────────────────────────────────────────────
        foreach (SeedBook book in SeedBooks)
        {
            string fileName = $"{SanitizeFileName(book.Title)}.epub";
            string filePath = Path.Combine(watchDir, fileName);

            if (File.Exists(filePath))
            {
                skipped++;
                logger.LogDebug("Seed file already exists, skipping: {Path}", filePath);
                continue;
            }

            byte[] epub = EpubBuilder.Create(
                book.Title, book.Author, book.Isbn, book.Year, book.Description,
                book.Publisher, book.Language, book.AdditionalAuthors,
                book.Series, book.SeriesPosition);

            await File.WriteAllBytesAsync(filePath, epub);

            created.Add(fileName);
            logger.LogInformation(
                "Seed EPUB created: {Path} ({Size} bytes) [{Category}]",
                filePath, epub.Length, book.TestCategory ?? "Uncategorised");
        }

        // ── Seed MP3 Audiobooks ─────────────────────────────────────────────
        foreach (SeedAudiobook ab in SeedAudiobooks)
        {
            // Use narrator in filename to distinguish multiple editions of the same title.
            string fileName = $"{SanitizeFileName(ab.Title)} - {SanitizeFileName(ab.Narrator)}.mp3";
            string filePath = Path.Combine(watchDir, fileName);

            if (File.Exists(filePath))
            {
                skipped++;
                logger.LogDebug("Seed file already exists, skipping: {Path}", filePath);
                continue;
            }

            byte[] mp3 = Mp3Builder.Create(
                ab.Title, ab.Artist, narrator: ab.Narrator,
                year: ab.Year, language: ab.Language,
                series: ab.Series, seriesPosition: ab.SeriesPosition,
                asin: ab.Asin);

            await File.WriteAllBytesAsync(filePath, mp3);

            created.Add(fileName);
            logger.LogInformation(
                "Seed MP3 created: {Path} ({Size} bytes) [{Category}]",
                filePath, mp3.Length, ab.TestCategory ?? "Uncategorised");
        }

        int totalSeed = SeedBooks.Length + SeedAudiobooks.Length;
        string message = created.Count > 0
            ? $"{created.Count} files dropped into Watch Folder. Ingestion will begin automatically."
            : "All seed files already exist in the Watch Folder.";

        return Results.Ok(new
        {
            files_created = created.Count,
            files_skipped = skipped,
            epubs_total = SeedBooks.Length,
            audiobooks_total = SeedAudiobooks.Length,
            total_seed_files = totalSeed,
            watch_directory = watchDir,
            files = created,
            message
        });
    }

    // ── POST /dev/wipe ──────────────────────────────────────────────────────

    private static async Task<IResult> WipeAsync(
        IDatabaseConnection db,
        IOptions<IngestionOptions> options,
        IIngestionEngine ingestionEngine,
        ILogger<Program> logger)
    {
        var wiped = new List<string>();

        // 1. Stop the ingestion engine so it doesn't try to process files during wipe.
        try
        {
            await ingestionEngine.StopAsync(CancellationToken.None);
            logger.LogInformation("[Wipe] Ingestion engine stopped");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Wipe] Failed to stop ingestion engine — continuing");
        }

        // 2. Wipe the library root (organized files + .staging/).
        string? libraryRoot = options.Value.LibraryRoot;
        if (!string.IsNullOrWhiteSpace(libraryRoot) && Directory.Exists(libraryRoot))
        {
            try
            {
                int count = WipeDirectoryContents(libraryRoot, logger);
                wiped.Add($"Library root ({libraryRoot}): {count} items deleted");
            }
            catch (Exception ex)
            {
                wiped.Add($"Library root ({libraryRoot}): FAILED — {ex.Message}");
                logger.LogError(ex, "[Wipe] Failed to wipe library root");
            }
        }
        else
        {
            wiped.Add("Library root: not configured or does not exist — skipped");
        }

        // 3. Wipe the watch folder.
        string? watchDir = options.Value.WatchDirectory;
        if (!string.IsNullOrWhiteSpace(watchDir) && Directory.Exists(watchDir))
        {
            try
            {
                int count = WipeDirectoryContents(watchDir, logger);
                wiped.Add($"Watch folder ({watchDir}): {count} items deleted");
            }
            catch (Exception ex)
            {
                wiped.Add($"Watch folder ({watchDir}): FAILED — {ex.Message}");
                logger.LogError(ex, "[Wipe] Failed to wipe watch folder");
            }
        }
        else
        {
            wiped.Add("Watch folder: not configured or does not exist — skipped");
        }

        // 4. Wipe database by dropping all tables and re-creating the schema.
        //    File deletion doesn't work because multiple services hold SQLite connections.
        //    SQL-level drop + recreate works with active connections.
        try
        {
            await db.AcquireWriteLockAsync();
            try
            {
                var conn = db.Open();

                // Disable foreign keys temporarily so we can drop tables in any order.
                using (var fkOff = conn.CreateCommand())
                {
                    fkOff.CommandText = "PRAGMA foreign_keys = OFF;";
                    fkOff.ExecuteNonQuery();
                }

                // Discover all user tables and drop them.
                var tables = new List<string>();
                using (var listCmd = conn.CreateCommand())
                {
                    listCmd.CommandText =
                        "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';";
                    using var reader = listCmd.ExecuteReader();
                    while (reader.Read())
                        tables.Add(reader.GetString(0));
                }

                foreach (string table in tables)
                {
                    using var dropCmd = conn.CreateCommand();
                    dropCmd.CommandText = $"DROP TABLE IF EXISTS [{table}];";
                    dropCmd.ExecuteNonQuery();
                }

                // Also drop FTS virtual tables (sqlite_master type='table' doesn't always list them).
                using (var ftsCmd = conn.CreateCommand())
                {
                    ftsCmd.CommandText = "DROP TABLE IF EXISTS search_index;";
                    ftsCmd.ExecuteNonQuery();
                }

                // Re-enable foreign keys.
                using (var fkOn = conn.CreateCommand())
                {
                    fkOn.CommandText = "PRAGMA foreign_keys = ON;";
                    fkOn.ExecuteNonQuery();
                }

                // Vacuum to reclaim space from dropped tables.
                using (var vacuumCmd = conn.CreateCommand())
                {
                    vacuumCmd.CommandText = "VACUUM;";
                    vacuumCmd.ExecuteNonQuery();
                }

                // Re-create all tables and run migrations.
                db.InitializeSchema();
                db.RunStartupChecks();

                wiped.Add($"Database: dropped {tables.Count} tables and reinitialized schema");
                logger.LogInformation("[Wipe] Database wiped: dropped {Count} tables, schema reinitialized", tables.Count);
            }
            finally
            {
                db.ReleaseWriteLock();
            }
        }
        catch (Exception ex)
        {
            wiped.Add($"Database: FAILED — {ex.Message}");
            logger.LogError(ex, "[Wipe] Failed to wipe database");
        }

        // 5. Restart the ingestion engine.
        try
        {
            ingestionEngine.Start();
            logger.LogInformation("[Wipe] Ingestion engine restarted");
            wiped.Add("Ingestion engine: restarted");
        }
        catch (Exception ex)
        {
            wiped.Add($"Ingestion engine restart: FAILED — {ex.Message}");
            logger.LogWarning(ex, "[Wipe] Failed to restart ingestion engine");
        }

        return Results.Ok(new
        {
            message = "Wipe complete. Database reinitialized. Ready for seeding.",
            details = wiped
        });
    }

    // ── POST /dev/full-test ─────────────────────────────────────────────────

    private static async Task<IResult> FullTestAsync(
        IDatabaseConnection db,
        IOptions<IngestionOptions> options,
        IIngestionEngine ingestionEngine,
        ILogger<Program> logger)
    {
        logger.LogInformation("[FullTest] Starting full ingestion test: wipe → seed → scan");

        // Step 1: Wipe
        var wipeResult = await WipeAsync(db, options, ingestionEngine, logger);

        // Step 2: Seed
        var seedResult = await SeedLibraryAsync(options, logger);

        // Step 3: Trigger a directory scan so the file watcher picks up the seeded files.
        //         FSW only detects NEW events; files already present when the watcher
        //         started require an explicit scan to generate synthetic "Created" events.
        string? watchDir = options.Value.WatchDirectory;
        if (!string.IsNullOrWhiteSpace(watchDir) && Directory.Exists(watchDir))
        {
            ingestionEngine.ScanDirectory(watchDir);
            logger.LogInformation("[FullTest] ScanDirectory triggered for {Path}", watchDir);
        }

        return Results.Ok(new
        {
            message = "Full test initiated: database wiped, library cleared, seed files dropped, " +
                      "directory scan triggered. Ingestion pipeline will process files automatically. " +
                      "Monitor via GET /ingestion/batches and SignalR intercom.",
            wipe = wipeResult,
            seed = seedResult,
            total_test_files = SeedBooks.Length + SeedAudiobooks.Length,
            epubs = SeedBooks.Length,
            audiobooks = SeedAudiobooks.Length,
            next_steps = new[]
            {
                "Watch ingestion progress: GET /ingestion/batches",
                "Check registry: GET /registry/items?page=1&pageSize=50",
                "Check review queue: GET /review/pending",
                "Check activity: GET /activity/recent",
                "Monitor SignalR events: MediaAdded, MetadataHarvested, ReviewItemCreated"
            }
        });
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Deletes all files and subdirectories inside a directory, preserving the directory itself.
    /// Returns the number of items deleted.
    /// </summary>
    private static int WipeDirectoryContents(string dirPath, ILogger logger)
    {
        int count = 0;
        var dir = new DirectoryInfo(dirPath);

        foreach (FileInfo file in dir.GetFiles("*", SearchOption.AllDirectories))
        {
            try
            {
                file.Attributes = FileAttributes.Normal; // Clear read-only
                file.Delete();
                count++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[Wipe] Could not delete file: {Path}", file.FullName);
            }
        }

        foreach (DirectoryInfo sub in dir.GetDirectories())
        {
            try
            {
                sub.Delete(recursive: true);
                count++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[Wipe] Could not delete directory: {Path}", sub.FullName);
            }
        }

        logger.LogInformation("[Wipe] Wiped {Count} items from {Path}", count, dirPath);
        return count;
    }

    /// <summary>
    /// Removes characters that are invalid in file names.
    /// </summary>
    private static string SanitizeFileName(string title)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(title.Length);
        foreach (char c in title)
        {
            sb.Append(invalid.Contains(c) ? '_' : c);
        }
        return sb.ToString();
    }
}
