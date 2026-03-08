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
    /// Seed book definitions — 7 classic novels with real ISBNs so the hydration
    /// pipeline (Apple Books, Google Books, Open Library) can find real cover art.
    /// </summary>
    private static readonly (string Title, string Author, string Isbn, int Year, string Description)[] SeedBooks =
    [
        (
            "Dune",
            "Frank Herbert",
            "9780441013593",
            1965,
            "Set on the desert planet Arrakis, Dune is the story of the boy Paul Atreides, heir to a noble family tasked with ruling an inhospitable world where the only thing of value is a spice capable of extending life and expanding consciousness."
        ),
        (
            "The Hobbit",
            "J.R.R. Tolkien",
            "9780547928227",
            1937,
            "Bilbo Baggins is a hobbit who enjoys a comfortable, unambitious life, rarely travelling further than the pantry of his hobbit-hole in Bag End. But his contentment is disturbed when the wizard Gandalf and a company of thirteen dwarves arrive on his doorstep."
        ),
        (
            "1984",
            "George Orwell",
            "9780451524935",
            1949,
            "Among the seminal texts of the 20th century, this novel is a profound statement about the human spirit and its resilience in the face of a totalitarian regime that seeks to control every aspect of life."
        ),
        (
            "Project Hail Mary",
            "Andy Weir",
            "9780593135204",
            2021,
            "Ryland Grace is the sole survivor on a desperate, last-chance mission. If he fails, humanity and the earth itself will perish. Except right now, he does not even know his own name."
        ),
        (
            "Neuromancer",
            "William Gibson",
            "9780441569595",
            1984,
            "The Matrix is a world within the world, a global consensus-hallucination, the representation of every byte of data in cyberspace. Case had been the sharpest data-thief in the business, until vengeful former employers crippled his nervous system."
        ),
        (
            "The Hitchhiker's Guide to the Galaxy",
            "Douglas Adams",
            "9780345391803",
            1979,
            "Seconds before the Earth is demolished to make way for a galactic freeway, Arthur Dent is plucked off the planet by his friend Ford Prefect, a researcher for the revised edition of The Hitchhiker's Guide to the Galaxy."
        ),
        (
            "Fahrenheit 451",
            "Ray Bradbury",
            "9781451673319",
            1953,
            "Guy Montag is a fireman. In his world, where television rules and literature is on the brink of extinction, firemen start fires rather than put them out. His job is to destroy the most illegal of commodities, the printed book."
        ),
    ];

    public static void MapDevSeedEndpoints(this WebApplication app)
    {
        RouteGroupBuilder group = app.MapGroup("/dev")
            .WithTags("Development");

        group.MapPost("/seed-library", SeedLibraryAsync)
            .WithSummary("Drop 7 classic book EPUBs into the Watch Folder for ingestion");
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

        foreach (var (title, author, isbn, year, description) in SeedBooks)
        {
            string fileName = $"{SanitizeFileName(title)}.epub";
            string filePath = Path.Combine(watchDir, fileName);

            // Skip if file already exists (idempotent).
            if (File.Exists(filePath))
            {
                skipped++;
                logger.LogDebug("Seed file already exists, skipping: {Path}", filePath);
                continue;
            }

            byte[] epub = EpubBuilder.Create(title, author, isbn, year, description);
            await File.WriteAllBytesAsync(filePath, epub);

            created.Add(fileName);
            logger.LogInformation("Seed EPUB created: {Path} ({Size} bytes)", filePath, epub.Length);
        }

        string message = created.Count > 0
            ? $"{created.Count} books dropped into Watch Folder. Ingestion will begin automatically."
            : "All seed books already exist in the Watch Folder.";

        return Results.Ok(new
        {
            files_created = created.Count,
            files_skipped = skipped,
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
        var sb = new System.Text.StringBuilder(title.Length);
        foreach (char c in title)
        {
            sb.Append(invalid.Contains(c) ? '_' : c);
        }
        return sb.ToString();
    }
}
