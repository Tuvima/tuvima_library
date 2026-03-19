using System.Text;
using MediaEngine.Ingestion.Models;
using Microsoft.Extensions.Options;

namespace MediaEngine.Api.DevSupport;

/// <summary>
/// Development-only endpoints for seeding the library with test data.
/// Registered conditionally when <c>ASPNETCORE_ENVIRONMENT == "Development"</c>.
/// </summary>
public static class DevSeedEndpoints
{
    /// <summary>
    /// A single seed book definition with all metadata fields needed by EpubBuilder.
    /// </summary>
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

    // ── Seed definitions ───────────────────────────────────────────────────
    // Organised by test category. Real ISBNs so the hydration pipeline can
    // fetch real cover art and metadata from Apple API, Google Books, etc.

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
            "Humanity has colonized the solar system. Jim Holden is XO of an pointice hauler that makes a horrifying discovery in the asteroid belt.",
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

        // ── Category 8: Publisher Metadata ──────────────────────────────────

        new("Frankenstein",
            "Mary Shelley",
            "9780141439471", 1818,
            "Obsessed with creating life itself, Victor Frankenstein plunders graveyards for the material to fashion a new being.",
            Publisher: "Lackington, Hughes, Harding, Mavor & Jones",
            TestCategory: "Publisher — very old book, long publisher name with special chars"),
    ];

    public static void MapDevSeedEndpoints(this WebApplication app)
    {
        RouteGroupBuilder group = app.MapGroup("/dev")
            .WithTags("Development");

        group.MapPost("/seed-library", SeedLibraryAsync)
            .WithSummary("Drop 22 test EPUBs into the Watch Folder for ingestion testing");
    }

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

        foreach (SeedBook book in SeedBooks)
        {
            string fileName = $"{SanitizeFileName(book.Title)}.epub";
            string filePath = Path.Combine(watchDir, fileName);

            // Skip if file already exists (idempotent).
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

        string message = created.Count > 0
            ? $"{created.Count} books dropped into Watch Folder. Ingestion will begin automatically."
            : "All seed books already exist in the Watch Folder.";

        return Results.Ok(new
        {
            files_created = created.Count,
            files_skipped = skipped,
            total_seed_books = SeedBooks.Length,
            watch_directory = watchDir,
            files = created,
            message
        });
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
