using System.Text;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
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
///   POST /dev/seed-library  — Drop test files into media-type-specific Watch Folders (comics excluded)
///   POST /dev/wipe           — Wipe DB, library root, watch folder, and reinitialize
///   POST /dev/full-test      — Wipe → Seed → return summary
/// </summary>
public static class DevSeedEndpoints
{
    /// <summary>A seed EPUB definition.</summary>
    /// <remarks>
    /// <para><c>ExpectedQid</c> — when set, the reconciliation pass asserts the
    /// resolved Wikidata QID exactly matches this value. Leave null for fixtures
    /// where any QID (or no QID) is acceptable.</para>
    /// <para><c>ExpectedCoverArt</c> — when true (default), the Vault display
    /// validation asserts that cover art was successfully downloaded for this
    /// item. Set to false for fixtures where no cover art is expected (e.g.
    /// placeholder titles or review-queue items).</para>
    /// </remarks>
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
        string? TestCategory = null,
        bool ExpectIdentified = true,
        string? ExpectedReviewTrigger = null,
        string? ExpectedReason = null,
        string? ExpectedProvider = null,
        string? ExpectedQid = null,
        bool ExpectedCoverArt = true);

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
        string? TestCategory = null,
        bool ExpectIdentified = true,
        string? ExpectedReviewTrigger = null,
        string? ExpectedReason = null,
        string? ExpectedProvider = null,
        string? ExpectedQid = null,
        bool ExpectedCoverArt = true);

    /// <summary>A seed MP4 movie/TV definition.</summary>
    private sealed record SeedVideo(
        string Title,
        string? Director,
        int Year,
        string MediaType,  // "Movie" or "TV"
        string? Series = null,
        int? SeasonNumber = null,
        int? EpisodeNumber = null,
        string? EpisodeTitle = null,
        string? FileNameOverride = null,
        string? TestCategory = null,
        bool ExpectIdentified = true,
        string? ExpectedReviewTrigger = null,
        string? ExpectedReason = null,
        string? ExpectedProvider = null,
        string? ExpectedQid = null,
        bool ExpectedCoverArt = true);

    /// <summary>A seed FLAC music track definition.</summary>
    private sealed record SeedMusic(
        string Title,
        string Artist,
        string? Album = null,
        int Year = 0,
        string? Genre = null,
        int? TrackNumber = null,
        string? TestCategory = null,
        bool ExpectIdentified = true,
        string? ExpectedReviewTrigger = null,
        string? ExpectedReason = null,
        string? ExpectedProvider = null,
        string? ExpectedQid = null,
        bool ExpectedCoverArt = true);

    /// <summary>A seed CBZ comic definition.</summary>
    private sealed record SeedComic(
        string Title,
        string? Writer = null,
        string? Series = null,
        int? Number = null,
        int Year = 0,
        string? Genre = null,
        string? Summary = null,
        string? Publisher = null,
        string? Penciller = null,
        string? TestCategory = null,
        bool ExpectIdentified = true,
        string? ExpectedReviewTrigger = null,
        string? ExpectedReason = null,
        string? ExpectedProvider = null,
        string? ExpectedQid = null,
        bool ExpectedCoverArt = true);

    // ── EPUB Seed definitions ────────────────────────────────────────────────
    // Real ISBNs so the hydration pipeline can fetch real cover art and metadata.

    private static readonly SeedBook[] SeedBooks =
    [
        // ── Category 1: Standard Cases (clean metadata, strong Wikidata presence) ──

        new("Dune",
            "Frank Herbert",
            "9780441013593", 1965,
            "Set on the desert planet Arrakis, Dune is the story of the boy Paul Atreides, heir to a noble family tasked with ruling an inhospitable world where the only thing of value is a spice capable of extending life and expanding consciousness.",
            TestCategory: "Standard",
            ExpectedQid: "Q190192"),

        new("Project Hail Mary",
            "Andy Weir",
            "9780593135204", 2021,
            "Ryland Grace is the sole survivor on a desperate, last-chance mission. If he fails, humanity and the earth itself will perish.",
            TestCategory: "Standard",
            ExpectedQid: "Q106852836"),

        new("The Hobbit",
            "J.R.R. Tolkien",
            "9780547928227", 1937,
            "Bilbo Baggins is a hobbit who enjoys a comfortable, unambitious life, rarely travelling further than the pantry of his hobbit-hole in Bag End.",
            TestCategory: "Standard",
            ExpectedQid: "Q74287"),

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
            TestCategory: "PenName — author also writes as Richard Bachman",
            ExpectedQid: "Q470937"),

        new("The Long Walk",
            "Richard Bachman",
            "9781501143823", 1979,
            "On the first day of May, one hundred teenage boys meet for an annual walking contest called The Long Walk.",
            TestCategory: "PenName — Stephen King writing as Richard Bachman",
            ExpectedQid: "Q384160"),

        // ── Category 3: Foreign Language ───────────────────────────────────

        new("Le Petit Prince",
            "Antoine de Saint-Exupéry",
            "9782070612758", 1943,
            "Un pilote, forcé d'atterrir dans le Sahara, rencontre un petit garçon venu d'une autre planète.",
            Language: "fr",
            TestCategory: "Foreign — French, accented author name",
            ExpectedQid: "Q25338"),

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
            TestCategory: "Series — position 1",
            ExpectedQid: "Q43361"),

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
            TestCategory: "Series — same author as The Hobbit, different series",
            ExpectedQid: "Q208002"),

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
            TestCategory: "Edge — minimal metadata: no ISBN, no year, no description",
            ExpectIdentified: false,
            ExpectedReviewTrigger: ReviewTrigger.PlaceholderTitle,
            ExpectedReason: "Placeholder title 'Untitled Book' with no real metadata should trigger review",
            ExpectedCoverArt: false),

        new("A",
            "B",
            "", 2000,
            "A very short title.",
            TestCategory: "Edge — extremely short title and author, no bridge IDs",
            ExpectIdentified: false,
            ExpectedReviewTrigger: ReviewTrigger.PlaceholderTitle,
            ExpectedReason: "Single-character title with no ISBN should trigger placeholder review",
            ExpectedCoverArt: false),

        new("The Extraordinary & Fantastical Adventures of Dr. Enid Hartwell-Smythe III: A Most Peculiar Chronicle",
            "Reginald Fortescue-Pemberton IV",
            "9780000000001", 2020,
            "A book with an extraordinarily long title and author name designed to test truncation, file naming, and display in constrained UI elements.",
            TestCategory: "Edge — very long title and author, special chars (& : .)",
            ExpectIdentified: false,
            ExpectedReviewTrigger: ReviewTrigger.RetailMatchFailed,
            ExpectedReason: "Fictional book with synthetic ISBN correctly fails retail provider matching",
            ExpectedCoverArt: false),

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

    // ── MP4 Movie / TV Seed definitions ────────────────────────────────────
    // Titles chosen for strong TMDB + Wikidata presence.

    private static readonly SeedVideo[] SeedVideos =
    [
        // ── Movies ──────────────────────────────────────────────────────────

        new("Blade Runner 2049", "Denis Villeneuve", 2017, "Movie",
            TestCategory: "Movie — same director as Dune films, strong TMDB match",
            ExpectedQid: "Q21500755"),

        new("The Matrix", "Lana Wachowski", 1999, "Movie",
            TestCategory: "Movie — classic, strong Wikidata presence",
            ExpectedQid: "Q83495"),

        new("Arrival", "Denis Villeneuve", 2016, "Movie",
            TestCategory: "Movie — same director as Blade Runner, cross-reference test",
            ExpectedQid: "Q20382729"),

        new("Spirited Away", "Hayao Miyazaki", 2001, "Movie",
            TestCategory: "Movie — Japanese film, foreign language metadata",
            ExpectedQid: "Q155653"),

        new("Interstellar", "Christopher Nolan", 2014, "Movie",
            TestCategory: "Movie — strong TMDB match, popular film",
            ExpectedQid: "Q13417189"),

        new("The Shawshank Redemption", "Frank Darabont", 1994, "Movie",
            TestCategory: "Movie — Stephen King adaptation (cross-ref with books)",
            ExpectedQid: "Q172241"),

        // ── TV Episodes ─────────────────────────────────────────────────────

        new("Breaking Bad", null, 2008, "TV",
            Series: "Breaking Bad", SeasonNumber: 1, EpisodeNumber: 1,
            TestCategory: "TV — S01E01, strong TMDB match",
            ExpectedProvider: "tmdb"),

        new("Breaking Bad", null, 2008, "TV",
            Series: "Breaking Bad", SeasonNumber: 1, EpisodeNumber: 2,
            TestCategory: "TV — S01E02, same series grouping test",
            ExpectedProvider: "tmdb"),

        new("The Expanse", null, 2015, "TV",
            Series: "The Expanse", SeasonNumber: 1, EpisodeNumber: 1,
            TestCategory: "TV — cross-ref with book series (Leviathan Wakes)",
            ExpectedProvider: "tmdb"),

        new("Shogun", null, 2024, "TV",
            Series: "Shogun", SeasonNumber: 1, EpisodeNumber: 1,
            TestCategory: "TV — recent series, cross-media potential",
            ExpectedProvider: "tmdb"),

        // ── New TV fixtures: filename pattern coverage (Phase: scoring fix) ──
        // Each fixture targets a different on-disk filename pattern so the
        // VideoProcessor's TV regex variants and the structural-bonus scoring
        // path are all exercised end-to-end by the integration test.

        new("Pilot", null, 2008, "TV",
            Series: "Breaking Bad", SeasonNumber: 1, EpisodeNumber: 1,
            EpisodeTitle: "Pilot",
            FileNameOverride: "Breaking Bad/Season 01/Breaking Bad - S01E01 - Pilot.mp4",
            TestCategory: "TV pattern — show + SxxExx + episode title in nested folder",
            ExpectedProvider: "tmdb"),

        new("Anjin", null, 2024, "TV",
            Series: "Shogun", SeasonNumber: 1, EpisodeNumber: 1,
            EpisodeTitle: "Anjin",
            FileNameOverride: "Shogun (2024)/Season 01/Shogun - S01E01 - Anjin.mp4",
            TestCategory: "TV pattern — show with year suffix folder + SxxExx + episode title",
            ExpectedProvider: "tmdb"),

        new("Chapter 1: The Mandalorian", null, 2019, "TV",
            Series: "The Mandalorian", SeasonNumber: 1, EpisodeNumber: 1,
            EpisodeTitle: "Chapter 1 - The Mandalorian",
            FileNameOverride: "The Mandalorian/Season 01/S01E01 - Chapter 1 - The Mandalorian.mp4",
            TestCategory: "TV pattern — leading SxxExx (no show prefix), show inferred from folder",
            ExpectedProvider: "tmdb"),

        new("The Mathematician's Ghost", null, 2021, "TV",
            Series: "Foundation", SeasonNumber: 1, EpisodeNumber: 3,
            EpisodeTitle: "The Mathematician's Ghost",
            FileNameOverride: "Foundation/Season 01/Foundation - S01E03 - The Mathematician's Ghost.mp4",
            TestCategory: "TV pattern — non-pilot episode with possessive in title",
            ExpectedProvider: "tmdb"),

        new("The You You Are", null, 2022, "TV",
            Series: "Severance", SeasonNumber: 1, EpisodeNumber: 4,
            EpisodeTitle: "The You You Are",
            FileNameOverride: "Severance/Season 01/Severance.S01E04.The.You.You.Are.mp4",
            TestCategory: "TV pattern — dot-separated filename convention",
            ExpectedProvider: "tmdb"),
    ];

    // ── FLAC Music Seed definitions ────────────────────────────────────────
    // FLAC is unambiguously routed to Music by AudioProcessor (0.95 confidence).

    private static readonly SeedMusic[] SeedMusicTracks =
    [
        // ── Category 1: Standard (strong Apple Music presence) ─────────────
        new("Bohemian Rhapsody", "Queen",
            Album: "A Night at the Opera", Year: 1975, Genre: "Rock", TrackNumber: 11,
            TestCategory: "Music — classic track, strong Apple Music match"),

        new("Clair de Lune", "Claude Debussy",
            Album: "Suite bergamasque", Year: 1905, Genre: "Classical", TrackNumber: 3,
            TestCategory: "Music — classical, foreign artist name"),

        new("Lose Yourself", "Eminem",
            Album: "8 Mile: Music from and Inspired by the Motion Picture", Year: 2002, Genre: "Hip-Hop", TrackNumber: 1,
            TestCategory: "Music — soundtrack, must resolve to 8 Mile OST via Apple Music"),

        new("Nuvole Bianche", "Ludovico Einaudi",
            Album: "Una Mattina", Year: 2004, Genre: "Classical", TrackNumber: 6,
            TestCategory: "Music — contemporary classical, Italian artist"),

        new("Across the Stars", "John Williams",
            Album: "Star Wars: Attack of the Clones", Year: 2002, Genre: "Soundtrack", TrackNumber: 3,
            TestCategory: "Music — film soundtrack, franchise cross-ref"),

        // ── Category 2: Album grouping (multiple tracks, same album) ──────
        new("You're My Best Friend", "Queen",
            Album: "A Night at the Opera", Year: 1975, Genre: "Rock", TrackNumber: 4,
            TestCategory: "Music — same album as Bohemian Rhapsody, Hub grouping test"),

        new("Death on Two Legs", "Queen",
            Album: "A Night at the Opera", Year: 1975, Genre: "Rock", TrackNumber: 1,
            TestCategory: "Music — same album, track 1, Hub grouping test"),

        // ── Category 3: Multi-artist / featured / collaboration ───────────
        new("Under Pressure", "Queen & David Bowie",
            Album: "Hot Space", Year: 1982, Genre: "Rock", TrackNumber: 11,
            TestCategory: "Music — dual artist, ampersand separator"),

        new("Stan", "Eminem",
            Album: "The Marshall Mathers LP", Year: 2000, Genre: "Hip-Hop", TrackNumber: 3,
            TestCategory: "Music — same artist as Lose Yourself, different album"),

        // ── Category 4: Foreign language / non-Latin ──────────────────────
        new("La Vie en rose", "Édith Piaf",
            Album: "La Vie en rose", Year: 1947, Genre: "Chanson", TrackNumber: 1,
            TestCategory: "Music — French, accented artist name, classic",
            ExpectIdentified: false,
            ExpectedReviewTrigger: ReviewTrigger.RetailMatchFailed,
            ExpectedReason: "Apple Music localized search struggles with foreign-language classics; needs manual review"),

        new("Für Elise", "Ludwig van Beethoven",
            Album: "Beethoven: Piano Pieces", Year: 1810, Genre: "Classical", TrackNumber: 1,
            TestCategory: "Music — German umlaut in title, historical classical"),

        new("99 Luftballons", "Nena",
            Album: "99 Luftballons", Year: 1983, Genre: "New Wave", TrackNumber: 1,
            TestCategory: "Music — German title, one-name artist"),

        // ── Category 5: Disambiguation / common titles ────────────────────
        new("Yesterday", "The Beatles",
            Album: "Help!", Year: 1965, Genre: "Pop", TrackNumber: 13,
            TestCategory: "Music — extremely common title, must resolve to Beatles version"),

        new("Imagine", "John Lennon",
            Album: "Imagine", Year: 1971, Genre: "Pop", TrackNumber: 1,
            TestCategory: "Music — album same name as track, iconic single"),

        // ── Category 6: Instrumental / soundtrack / orchestral ────────────
        new("The Imperial March", "John Williams",
            Album: "Star Wars: The Empire Strikes Back", Year: 1980, Genre: "Soundtrack", TrackNumber: 3,
            TestCategory: "Music — same artist as Across the Stars, different franchise entry"),

        new("In the Hall of the Mountain King", "Edvard Grieg",
            Album: "Peer Gynt Suite No. 1", Year: 1875, Genre: "Classical", TrackNumber: 4,
            TestCategory: "Music — public domain classical, Norwegian composer"),

        // ── Category 7: Edge cases ────────────────────────────────────────
        new("4'33\"", "John Cage",
            Album: "John Cage: 4'33\"", Year: 1952, Genre: "Avant-Garde", TrackNumber: 1,
            TestCategory: "Edge — special chars in title (apostrophe + quotes), silent piece"),

        new("MMMBop", "Hanson",
            Album: "Middle of Nowhere", Year: 1997, Genre: "Pop", TrackNumber: 1,
            TestCategory: "Edge — unusual capitalization, 90s one-hit wonder"),

        new("Take Five", "Dave Brubeck",
            Album: "Time Out", Year: 1959, Genre: "Jazz", TrackNumber: 4,
            TestCategory: "Music — jazz standard, strong Apple Music presence"),

        new("Smells Like Teen Spirit", "Nirvana",
            Album: "Nevermind", Year: 1991, Genre: "Grunge", TrackNumber: 1,
            TestCategory: "Music — 90s rock, strong Wikidata QID presence"),
    ];

    // ── CBZ Comic Seed definitions ─────────────────────────────────────────
    // Comics with ComicInfo.xml metadata — tests the new ComicProcessor parsing.

    private static readonly SeedComic[] SeedComics =
    [
        // Comics expectations: all four are marked ExpectIdentified=false because the
        // Metron provider requires credentials that are not present in the default
        // ComicVine is the active comics provider. Distinctive titles (Akira,
        // Sandman) resolve via retail + Wikidata text reconciliation to auto-accept.
        // "Batman: Year One Part 1" fails retail — the "Part 1" suffix throws off
        // word overlap scoring against the collected edition title. "Saga Chapter One"
        // gets a retail match but Wikidata bridge fails (P5905 issue-level ID mismatch).
        // Both land in the Action Center for manual review.
        new("Batman: Year One Part 1", Writer: "Frank Miller",
            Series: "Batman", Number: 404, Year: 1987, Genre: "Superhero",
            Summary: "Bruce Wayne returns to Gotham City after years abroad.",
            Publisher: "DC Comics", Penciller: "David Mazzucchelli",
            TestCategory: "Comic — classic DC, series with issue number",
            ExpectIdentified: false,
            ExpectedReviewTrigger: ReviewTrigger.RetailMatchFailed,
            ExpectedReason: "Title 'Part 1' suffix reduces word overlap below match threshold"),

        new("Saga Chapter One", Writer: "Brian K. Vaughan",
            Series: "Saga", Number: 1, Year: 2012, Genre: "Science Fiction, Fantasy",
            Summary: "A new epic from the creators of Y: The Last Man.",
            Publisher: "Image Comics", Penciller: "Fiona Staples",
            TestCategory: "Comic — Image Comics, multi-genre",
            ExpectIdentified: false,
            ExpectedReviewTrigger: ReviewTrigger.WikidataBridgeFailed,
            ExpectedReason: "ComicVine issue-level ID not in Wikidata P5905"),

        new("The Sandman: Sleep of the Just", Writer: "Neil Gaiman",
            Series: "The Sandman", Number: 1, Year: 1989, Genre: "Fantasy, Horror",
            Summary: "Morpheus, the King of Dreams, is captured and held prisoner for 70 years.",
            Publisher: "DC Comics/Vertigo", Penciller: "Sam Kieth",
            TestCategory: "Comic — Neil Gaiman (cross-ref with Good Omens book)",
            ExpectIdentified: true),

        new("Akira Vol 1", Writer: "Katsuhiro Otomo",
            Series: "Akira", Number: 1, Year: 1982, Genre: "Science Fiction",
            Summary: "In the year 2019, Neo-Tokyo has risen from the ashes of World War III.",
            Publisher: "Kodansha", Penciller: "Katsuhiro Otomo",
            TestCategory: "Comic — manga, Japanese creator",
            ExpectIdentified: true),
    ];

    // ── Supported test media types and their provider health-check URLs ────
    // Provider → media types it gates. If the provider's API endpoint is unreachable,
    // those media types are skipped with a reason in the response.

    private static readonly string[] AllTestableTypes = ["books", "audiobooks", "movies", "tv", "music", "comics"];

    private static readonly Dictionary<string, string[]> ProviderToTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["apple_api"]   = ["books", "audiobooks", "music"],
        ["tmdb"]        = ["movies", "tv"],
        ["comicvine"]   = ["comics"],
    };

    private static readonly Dictionary<string, string> ProviderHealthUrls = new(StringComparer.OrdinalIgnoreCase)
    {
        ["apple_api"]   = "https://itunes.apple.com/search?term=test&limit=1",
        ["tmdb"]        = "https://api.themoviedb.org/3/configuration",
        ["comicvine"]   = "https://comicvine.gamespot.com/api/search/?query=batman&resources=issue&limit=1&format=json&api_key=placeholder",
    };

    /// <summary>
    /// Parse the <c>?types=books,comics,music</c> query parameter.
    /// Returns a normalised set of requested types (default: all).
    /// </summary>
    private static HashSet<string> ParseTypes(HttpContext context)
    {
        if (context.Request.Query.TryGetValue("types", out var typesParam) && !string.IsNullOrWhiteSpace(typesParam))
        {
            return typesParam.ToString()
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(t => t.ToLowerInvariant())
                .Where(t => AllTestableTypes.Contains(t))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        return new HashSet<string>(AllTestableTypes, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Probe each provider's API endpoint in parallel. Returns a dictionary of
    /// provider name → (healthy, reason). Timeout: 8 seconds per provider.
    /// Credentials are read from the loaded provider config (secrets applied) so no
    /// credentials are ever hardcoded here. A 2xx response is required — 401 is
    /// treated as "key missing or invalid", not as "healthy".
    /// </summary>
    private static async Task<Dictionary<string, (bool Healthy, string Reason)>> CheckProviderHealthAsync(
        ILogger logger,
        Storage.Contracts.IConfigurationLoader configLoader)
    {
        var results = new Dictionary<string, (bool, string)>(StringComparer.OrdinalIgnoreCase);
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("TuvimaLibrary/1.0 (integration-test)");

        var tasks = ProviderHealthUrls.Select(async kvp =>
        {
            try
            {
                // Load provider config — secrets (api_key, username, password) are applied
                // automatically by ConfigurationDirectoryLoader.ApplySecrets().
                var providerConfig = configLoader.LoadProvider(kvp.Key);
                var http = providerConfig?.HttpClient;
                var delivery = http?.ApiKeyDelivery?.ToLowerInvariant() ?? "";

                // Resolve the effective health-check URL.
                // For query-param delivery, append the key directly to the URL.
                string healthUrl = kvp.Value;
                if (!string.IsNullOrWhiteSpace(http?.ApiKey) && delivery is "query" or "query_param")
                {
                    var paramName = string.IsNullOrWhiteSpace(http.ApiKeyParamName) ? "api_key" : http.ApiKeyParamName;
                    var separator = healthUrl.Contains('?') ? "&" : "?";
                    healthUrl = healthUrl + separator + paramName + "=" + Uri.EscapeDataString(http.ApiKey);
                }

                // Build a per-request message so auth headers don't bleed across providers.
                using var req = new HttpRequestMessage(HttpMethod.Get, healthUrl);

                if (http is not null)
                {
                    if (!string.IsNullOrWhiteSpace(http.ApiKey) && delivery == "bearer")
                    {
                        req.Headers.Authorization =
                            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", http.ApiKey);
                    }
                    else if (!string.IsNullOrWhiteSpace(http.Username) && !string.IsNullOrWhiteSpace(http.Password))
                    {
                        var creds = Convert.ToBase64String(
                            System.Text.Encoding.UTF8.GetBytes($"{http.Username}:{http.Password}"));
                        req.Headers.Authorization =
                            new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", creds);
                    }
                }

                using var response = await httpClient.SendAsync(req);

                // A 2xx response confirms the endpoint is reachable AND the credentials work.
                // 401 → key missing or invalid; treat as unhealthy so the seed skips that type.
                bool ok = response.IsSuccessStatusCode;
                string reason = ok ? $"HTTP {(int)response.StatusCode}" : $"HTTP {(int)response.StatusCode} — key missing or invalid";
                return (kvp.Key, Healthy: ok, Reason: reason);
            }
            catch (Exception ex)
            {
                return (kvp.Key, Healthy: false, Reason: $"{ex.GetType().Name}: {ex.Message}");
            }
        });

        foreach (var result in await Task.WhenAll(tasks))
        {
            results[result.Key] = (result.Healthy, result.Reason);
            logger.LogInformation("[HealthCheck] {Provider}: {Status} ({Reason})", result.Key,
                result.Healthy ? "HEALTHY" : "UNAVAILABLE", result.Reason);
        }

        return results;
    }

    /// <summary>
    /// Given the requested types and provider health results, compute the set of
    /// types that are actually active (requested AND provider healthy).
    /// Also returns skip reasons for types that were excluded.
    /// </summary>
    private static (HashSet<string> ActiveTypes, Dictionary<string, string> SkipReasons) ResolveActiveTypes(
        HashSet<string> requestedTypes,
        Dictionary<string, (bool Healthy, string Reason)> health)
    {
        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var skipped = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var type in AllTestableTypes)
        {
            if (!requestedTypes.Contains(type))
            {
                skipped[type] = "Not requested";
                continue;
            }

            // Find which provider gates this type
            var gatingProvider = ProviderToTypes
                .Where(kvp => kvp.Value.Contains(type, StringComparer.OrdinalIgnoreCase))
                .Select(kvp => kvp.Key)
                .FirstOrDefault();

            if (gatingProvider is null)
            {
                active.Add(type); // No provider gate — always active
                continue;
            }

            if (health.TryGetValue(gatingProvider, out var status) && status.Healthy)
                active.Add(type);
            else
                skipped[type] = $"Provider '{gatingProvider}' unavailable ({(health.TryGetValue(gatingProvider!, out var s) ? s.Reason : "unknown")})";
        }

        return (active, skipped);
    }

    // ── Endpoint registration ────────────────────────────────────────────────

    public static void MapDevSeedEndpoints(this WebApplication app)
    {
        RouteGroupBuilder group = app.MapGroup("/dev")
            .WithTags("Development");

        group.MapGet("/check-keys", CheckKeysAsync)
            .WithSummary("Probe each configured provider with real credentials — confirms all API keys are valid before seeding");

        group.MapPost("/seed-library", SeedLibraryAsync)
            .WithSummary($"Drop up to {SeedBooks.Length + SeedAudiobooks.Length + SeedVideos.Length + SeedMusicTracks.Length + SeedComics.Length} test files into Watch Folders (?types=books,comics,… to filter; providers health-checked automatically)");

        group.MapPost("/wipe", WipeAsync)
            .WithSummary("Wipe database, library root, and watch folder — then reinitialize a fresh DB");

        group.MapPost("/full-test", FullTestAsync)
            .WithSummary("Wipe everything → seed test files → return per-type summary (?types= to filter; ?wipe=false to skip wipe and add files to existing DB)");

        group.MapGet("/pipeline-status", PipelineStatusAsync)
            .WithSummary("Show identity job counts by state + details of non-Completed jobs");
    }

    // ── GET /dev/check-keys ─────────────────────────────────────────────────

    private static async Task<IResult> CheckKeysAsync(
        Storage.Contracts.IConfigurationLoader configLoader,
        ILogger<Program> logger)
    {
        logger.LogInformation("[CheckKeys] Probing all configured provider API keys");

        var health = await CheckProviderHealthAsync(logger, configLoader);

        // Summarise per-provider with a human-readable diagnosis.
        var summary = health.ToDictionary(
            kvp => kvp.Key,
            kvp => new
            {
                status   = kvp.Value.Healthy ? "OK" : "FAIL",
                reason   = kvp.Value.Reason,
                affects  = ProviderToTypes.TryGetValue(kvp.Key, out var types) ? types : Array.Empty<string>(),
            });

        bool allHealthy = health.Values.All(v => v.Healthy);
        string verdict = allHealthy
            ? "All provider keys verified — ready to seed."
            : "One or more providers failed. Fill in the missing keys in config/secrets/ before seeding.";

        return Results.Ok(new
        {
            verdict,
            all_healthy = allHealthy,
            providers = summary,
        });
    }

    // ── POST /dev/seed-library ───────────────────────────────────────────────

    private static async Task<IResult> SeedLibraryAsync(
        HttpContext context,
        IOptions<IngestionOptions> options,
        Storage.Contracts.IConfigurationLoader configLoader,
        ILogger<Program> logger)
    {
        var requestedTypes = ParseTypes(context);
        var health = await CheckProviderHealthAsync(logger, configLoader);
        var (activeTypes, skipReasons) = ResolveActiveTypes(requestedTypes, health);

        var created = new List<string>();
        var perTypeResults = new Dictionary<string, object>();
        int skipped = 0;

        // ── Seed EPUBs ──────────────────────────────────────────────────────
        var booksDir = ResolveWatchDirectory(configLoader, options, "Books");
        int booksCreated = 0;
        if (activeTypes.Contains("books") && !string.IsNullOrWhiteSpace(booksDir))
        {
            EnsureDirectory(booksDir, logger);
            foreach (SeedBook book in SeedBooks)
            {
                string fileName = $"{SanitizeFileName(book.Title)}.epub";
                string filePath = Path.Combine(booksDir, fileName);

                if (File.Exists(filePath)) { skipped++; continue; }

                byte[] epub = EpubBuilder.Create(
                    book.Title, book.Author, book.Isbn, book.Year, book.Description,
                    book.Publisher, book.Language, book.AdditionalAuthors,
                    book.Series, book.SeriesPosition);
                await File.WriteAllBytesAsync(filePath, epub);
                created.Add(fileName);
                booksCreated++;
                logger.LogInformation("Seed EPUB created: {Path} [{Category}]", filePath, book.TestCategory ?? "Uncategorised");
            }
        }
        perTypeResults["books"] = activeTypes.Contains("books")
            ? new { total = SeedBooks.Length, created = booksCreated, directory = booksDir ?? "not configured" }
            : (object)new { total = SeedBooks.Length, created = 0, skipped_reason = skipReasons.GetValueOrDefault("books", "Excluded") };

        // ── Seed MP3 Audiobooks ─────────────────────────────────────────────
        // Audiobooks share the Books library folder.
        int audiobooksCreated = 0;
        if (activeTypes.Contains("audiobooks") && !string.IsNullOrWhiteSpace(booksDir))
        {
            foreach (SeedAudiobook ab in SeedAudiobooks)
            {
                string fileName = $"{SanitizeFileName(ab.Title)} - {SanitizeFileName(ab.Narrator)}.mp3";
                string filePath = Path.Combine(booksDir, fileName);

                if (File.Exists(filePath)) { skipped++; continue; }

                byte[] mp3 = Mp3Builder.Create(
                    ab.Title, ab.Artist, narrator: ab.Narrator,
                    year: ab.Year, language: ab.Language,
                    series: ab.Series, seriesPosition: ab.SeriesPosition,
                    asin: ab.Asin);
                await File.WriteAllBytesAsync(filePath, mp3);
                created.Add(fileName);
                audiobooksCreated++;
                logger.LogInformation("Seed MP3 created: {Path} [{Category}]", filePath, ab.TestCategory ?? "Uncategorised");
            }
        }
        perTypeResults["audiobooks"] = activeTypes.Contains("audiobooks")
            ? new { total = SeedAudiobooks.Length, created = audiobooksCreated, directory = booksDir ?? "not configured" }
            : (object)new { total = SeedAudiobooks.Length, created = 0, skipped_reason = skipReasons.GetValueOrDefault("audiobooks", "Excluded") };

        // ── Seed MP4 Videos (Movies + TV) ─────────────────────────────────
        int moviesCreated = 0, tvCreated = 0;
        foreach (SeedVideo video in SeedVideos)
        {
            var typeKey = video.MediaType == "TV" ? "tv" : "movies";
            if (!activeTypes.Contains(typeKey)) continue;
            var category = video.MediaType == "TV" ? "TV" : "Movies";
            var videoDir = ResolveWatchDirectory(configLoader, options, category);
            if (string.IsNullOrWhiteSpace(videoDir)) continue;
            EnsureDirectory(videoDir, logger);

            string fileName;
            string filePath;
            if (!string.IsNullOrWhiteSpace(video.FileNameOverride))
            {
                // Allow nested relative paths (e.g. "Breaking Bad/Season 01/...mp4")
                // so the test harness can exercise filename patterns that depend on
                // parent-folder context (leading SxxExx, year suffix, etc.).
                var rel = video.FileNameOverride.Replace('/', Path.DirectorySeparatorChar);
                filePath = Path.Combine(videoDir, rel);
                fileName = Path.GetFileName(filePath);
                var parentDir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrWhiteSpace(parentDir))
                    EnsureDirectory(parentDir, logger);
            }
            else
            {
                if (video.MediaType == "TV" && video.SeasonNumber is not null && video.EpisodeNumber is not null)
                    fileName = $"{SanitizeFileName(video.Series ?? video.Title)} S{video.SeasonNumber:D2}E{video.EpisodeNumber:D2}.mp4";
                else
                    fileName = $"{SanitizeFileName(video.Title)} ({video.Year}).mp4";
                filePath = Path.Combine(videoDir, fileName);
            }

            if (File.Exists(filePath)) { skipped++; continue; }

            byte[] mp4 = Mp4Builder.Create(
                video.Title, video.Director, video.Year,
                showName: video.MediaType == "TV" ? video.Series : null,
                seasonNumber: video.SeasonNumber,
                episodeNumber: video.EpisodeNumber);
            await File.WriteAllBytesAsync(filePath, mp4);
            created.Add(fileName);
            if (video.MediaType == "TV") tvCreated++; else moviesCreated++;
            logger.LogInformation("Seed MP4 created: {Path} [{Category}]", filePath, video.TestCategory ?? "Uncategorised");
        }
        var moviesDir = ResolveWatchDirectory(configLoader, options, "Movies");
        var tvDir = ResolveWatchDirectory(configLoader, options, "TV");
        perTypeResults["movies"] = activeTypes.Contains("movies")
            ? new { total = SeedVideos.Count(v => v.MediaType == "Movie"), created = moviesCreated, directory = moviesDir ?? "not configured" }
            : (object)new { total = SeedVideos.Count(v => v.MediaType == "Movie"), created = 0, skipped_reason = skipReasons.GetValueOrDefault("movies", "Excluded") };
        perTypeResults["tv"] = activeTypes.Contains("tv")
            ? new { total = SeedVideos.Count(v => v.MediaType == "TV"), created = tvCreated, directory = tvDir ?? "not configured" }
            : (object)new { total = SeedVideos.Count(v => v.MediaType == "TV"), created = 0, skipped_reason = skipReasons.GetValueOrDefault("tv", "Excluded") };

        // ── Seed FLAC Music ───────────────────────────────────────────────
        var musicDir = ResolveWatchDirectory(configLoader, options, "Music");
        int musicCreated = 0;
        if (activeTypes.Contains("music") && !string.IsNullOrWhiteSpace(musicDir))
        {
            EnsureDirectory(musicDir, logger);
            foreach (SeedMusic track in SeedMusicTracks)
            {
                string fileName = $"{SanitizeFileName(track.Artist)} - {SanitizeFileName(track.Title)}.flac";
                string filePath = Path.Combine(musicDir, fileName);

                if (File.Exists(filePath)) { skipped++; continue; }

                byte[] flac = FlacBuilder.Create(
                    track.Title, track.Artist, track.Album,
                    track.Year, track.Genre, track.TrackNumber);
                await File.WriteAllBytesAsync(filePath, flac);
                created.Add(fileName);
                musicCreated++;
                logger.LogInformation("Seed FLAC created: {Path} [{Category}]", filePath, track.TestCategory ?? "Uncategorised");
            }
        }
        perTypeResults["music"] = activeTypes.Contains("music")
            ? new { total = SeedMusicTracks.Length, created = musicCreated, directory = musicDir ?? "not configured" }
            : (object)new { total = SeedMusicTracks.Length, created = 0, skipped_reason = skipReasons.GetValueOrDefault("music", "Excluded") };

        // ── Seed CBZ Comics ──────────────────────────────────────────────
        var comicsDir = ResolveWatchDirectory(configLoader, options, "Comics");
        int comicsCreated = 0;
        if (activeTypes.Contains("comics") && !string.IsNullOrWhiteSpace(comicsDir))
        {
            EnsureDirectory(comicsDir, logger);
            foreach (SeedComic comic in SeedComics)
            {
                string fileName = $"{SanitizeFileName(comic.Title)}.cbz";
                string filePath = Path.Combine(comicsDir, fileName);

                if (File.Exists(filePath)) { skipped++; continue; }

                byte[] cbz = CbzBuilder.Create(
                    comic.Title, comic.Writer, comic.Series, comic.Number,
                    comic.Year, comic.Genre, comic.Summary, comic.Publisher, comic.Penciller);
                await File.WriteAllBytesAsync(filePath, cbz);
                created.Add(fileName);
                comicsCreated++;
                logger.LogInformation("Seed CBZ created: {Path} [{Category}]", filePath, comic.TestCategory ?? "Uncategorised");
            }
        }
        perTypeResults["comics"] = activeTypes.Contains("comics")
            ? new { total = SeedComics.Length, created = comicsCreated, directory = comicsDir ?? "not configured" }
            : (object)new { total = SeedComics.Length, created = 0, skipped_reason = skipReasons.GetValueOrDefault("comics", "Excluded") };

        int totalSeeded = booksCreated + audiobooksCreated + moviesCreated + tvCreated + musicCreated + comicsCreated;
        int totalSeed = SeedBooks.Length + SeedAudiobooks.Length + SeedVideos.Length + SeedMusicTracks.Length + SeedComics.Length;
        string message = totalSeeded > 0
            ? $"{totalSeeded} files dropped into Watch Folders. Ingestion will begin automatically."
            : "All seed files already exist in the Watch Folders.";

        return Results.Ok(new
        {
            files_created = totalSeeded,
            files_skipped = skipped,
            total_seed_files = totalSeed,
            active_types = activeTypes.ToArray(),
            skipped_types = skipReasons,
            provider_health = health.ToDictionary(h => h.Key, h => h.Value.Healthy ? "healthy" : h.Value.Reason),
            per_type = perTypeResults,
            files = created,
            message
        });
    }

    // ── POST /dev/wipe ──────────────────────────────────────────────────────

    private static async Task<IResult> WipeAsync(
        IDatabaseConnection db,
        IOptions<IngestionOptions> options,
        Storage.Contracts.IConfigurationLoader configLoader,
        IIngestionEngine ingestionEngine,
        ILogger<Program> logger,
        bool startEngineAfterWipe = true)
    {
        var wiped = new List<string>();

        // 1. Pause the FSW so no new OS events fire during the wipe.
        //    PauseWatcher() stops the FileSystemWatcher and clears the FSW event
        //    buffer WITHOUT calling _debounce.Complete() — the consumer loop stays
        //    alive and can immediately process events queued by ScanDirectory after
        //    the wipe.  StopAsync() must NOT be used here because it completes the
        //    debounce channel, making subsequent ScanDirectory calls silently drop
        //    every event (ChannelClosedException swallowed in PromoteAsync).
        try
        {
            ingestionEngine.PauseWatcher();
            logger.LogInformation("[Wipe] Ingestion engine FSW paused");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Wipe] Failed to pause ingestion engine — continuing");
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

        // 3. Wipe library source paths from libraries.json + legacy watch folder.
        var libConfig = configLoader.LoadLibraries();
        foreach (var lib in libConfig.Libraries)
        {
            // Prefer SourcePaths (multi-path); fall back to legacy SourcePath.
            var paths = lib.SourcePaths?.Where(p => !string.IsNullOrWhiteSpace(p)).ToList()
                        ?? new List<string>();
            if (paths.Count == 0 && !string.IsNullOrWhiteSpace(lib.SourcePath))
                paths.Add(lib.SourcePath);

            foreach (var srcPath in paths)
            {
                if (Directory.Exists(srcPath))
                {
                    try
                    {
                        int count = WipeDirectoryContents(srcPath, logger);
                        wiped.Add($"Library source ({srcPath}): {count} items deleted");
                    }
                    catch (Exception ex)
                    {
                        wiped.Add($"Library source ({srcPath}): FAILED — {ex.Message}");
                        logger.LogError(ex, "[Wipe] Failed to wipe library source {Path}", srcPath);
                    }
                }
            }
        }

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

        // 5. Resume the FSW (conditionally — FullTestAsync defers this until after
        //    seeding so the FSW cannot fire on newly-written seed files before
        //    ScanDirectory has a chance to enqueue them directly).
        if (startEngineAfterWipe)
        {
            try
            {
                ingestionEngine.ResumeWatcher();
                logger.LogInformation("[Wipe] Ingestion engine FSW resumed");
                wiped.Add("Ingestion engine: FSW resumed");
            }
            catch (Exception ex)
            {
                wiped.Add($"Ingestion engine resume: FAILED — {ex.Message}");
                logger.LogWarning(ex, "[Wipe] Failed to resume ingestion engine");
            }
        }
        else
        {
            wiped.Add("Ingestion engine: FSW resume deferred (caller will resume after seeding)");
        }

        return Results.Ok(new
        {
            message = startEngineAfterWipe
                ? "Wipe complete. Database reinitialized. Ready for seeding."
                : "Wipe complete. Database reinitialized. Engine NOT restarted — caller will start after seeding.",
            details = wiped
        });
    }

    // ── POST /dev/full-test ─────────────────────────────────────────────────

    private static async Task<IResult> FullTestAsync(
        HttpContext context,
        IDatabaseConnection db,
        IOptions<IngestionOptions> options,
        Storage.Contracts.IConfigurationLoader configLoader,
        IIngestionEngine ingestionEngine,
        ILogger<Program> logger,
        bool wipe = true)
    {
        logger.LogInformation("[FullTest] Starting full ingestion test: {Mode}",
            wipe ? "wipe → seed → scan → start" : "seed → scan → start (no wipe)");

        // ── Step 1: Wipe (optional, default true) ────────────────────────────
        // Pass wipe=false from the batch files when the batch file has already
        // called POST /dev/wipe as a visible first step, so the coordinated
        // PauseWatcher → wipe → seed → scan → ResumeWatcher sequence still works
        // but the wipe step is explicit and auditable in the batch output.
        //
        // When wipe=true (default), the full sequence runs atomically here.
        // Either way, the FSW is paused before seeding so no spurious events fire.
        IResult? wipeResult = null;
        if (wipe)
        {
            wipeResult = await WipeAsync(db, options, configLoader, ingestionEngine, logger,
                startEngineAfterWipe: false);
        }
        else
        {
            // Caller already wiped and left the FSW paused — nothing to do here.
            logger.LogInformation("[FullTest] Wipe skipped (wipe=false) — assuming FSW already paused by caller");
        }

        // ── Step 2: Seed files (FSW is NOT watching — no spurious events) ─────
        var seedResult = await SeedLibraryAsync(context, options, configLoader, logger);

        // ── Step 3: Enqueue each seeded file directly into the pipeline ────────
        // ScanDirectory bypasses the 30-second FSW quiet-period buffer because it
        // stamps each event with a BatchId before calling Enqueue. Files flow
        // directly into the debounce queue and are processed without any timing
        // ambiguity — no Task.Delay, no settle uncertainty.
        var libConfig = configLoader.LoadLibraries();
        var scannedPaths = new List<string>();
        foreach (var lib in libConfig.Libraries)
        {
            // Prefer SourcePaths (multi-path); fall back to legacy SourcePath.
            var paths = lib.SourcePaths?.Where(p => !string.IsNullOrWhiteSpace(p)).ToList()
                        ?? new List<string>();
            if (paths.Count == 0 && !string.IsNullOrWhiteSpace(lib.SourcePath))
                paths.Add(lib.SourcePath);

            foreach (var srcPath in paths)
            {
                if (Directory.Exists(srcPath))
                {
                    ingestionEngine.ScanDirectory(srcPath, lib.IncludeSubdirectories);
                    scannedPaths.Add(srcPath);
                    logger.LogInformation("[FullTest] ScanDirectory triggered for {Path}", srcPath);
                }
            }
        }

        // Legacy watch folder scan — only if no per-library source paths were scanned.
        // When source_paths are configured, the legacy scan is redundant (the watch
        // folder is typically the parent of all source paths) and doubles the event
        // count, slowing down the pipeline.
        string? watchDir = options.Value.WatchDirectory;
        if (scannedPaths.Count == 0 && !string.IsNullOrWhiteSpace(watchDir) && Directory.Exists(watchDir))
        {
            ingestionEngine.ScanDirectory(watchDir);
            scannedPaths.Add(watchDir);
            logger.LogInformation("[FullTest] ScanDirectory triggered for legacy watch folder {Path}", watchDir);
        }

        // ── Step 4: Do NOT resume the FSW ─────────────────────────────────────
        // ScanDirectory already enqueued every seed file directly into the pipeline.
        // Resuming the FSW here causes a race: the watcher fires events for the
        // same files that are already being processed, and the lock probe fails
        // (the first processing attempt holds the file open), quarantining ~50
        // files. The FSW stays paused until the engine is restarted or a manual
        // POST /dev/resume-watcher is called.
        logger.LogInformation("[FullTest] FSW intentionally left paused — ScanDirectory handles all seed files");

        return Results.Ok(new
        {
            message = wipe
                ? "Full test initiated: database wiped, library cleared, seed files enqueued directly into the pipeline (race-free), FSW paused."
                : "Full test initiated (no wipe): seed files enqueued directly into the pipeline (race-free), FSW paused.",
            wipe_performed = wipe,
            wipe = wipeResult,
            seed = seedResult,
            scanned_directories = scannedPaths,
            total_test_files = SeedBooks.Length + SeedAudiobooks.Length + SeedVideos.Length + SeedMusicTracks.Length + SeedComics.Length,
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
    /// Resolves the correct watch directory for a media type by checking libraries.json.
    /// Falls back to the legacy watch directory if no specific library is configured.
    /// </summary>
    private static string? ResolveWatchDirectory(
        Storage.Contracts.IConfigurationLoader configLoader,
        IOptions<IngestionOptions> options,
        string mediaTypeCategory)
    {
        var libConfig = configLoader.LoadLibraries();

        // Find the library entry that matches this media type category.
        var lib = libConfig.Libraries.FirstOrDefault(l =>
            l.Category.Equals(mediaTypeCategory, StringComparison.OrdinalIgnoreCase));

        if (lib is not null && !string.IsNullOrWhiteSpace(lib.SourcePath))
            return lib.SourcePath;

        // Fall back to first library source or legacy watch directory.
        return libConfig.Libraries.Count > 0
            ? libConfig.Libraries[0].SourcePath
            : options.Value.WatchDirectory;
    }

    private static void EnsureDirectory(string path, ILogger logger)
    {
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
            logger.LogInformation("Created seed target folder at {Path}", path);
        }
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

    // ── Seed expectation model ────────────────────────────────────────────────

    /// <summary>
    /// Flattened expectation derived from a seed fixture, used by the integration
    /// test reconciliation pass to compare expected vs. actual pipeline outcomes.
    /// </summary>
    public sealed record SeedExpectation(
        string Title,
        string MediaType,
        string? Author,
        bool ExpectIdentified,
        string? ExpectedReviewTrigger,
        string? ExpectedReason,
        string? ExpectedProvider = null,
        string? ExpectedQid = null,
        bool ExpectedCoverArt = true);

    /// <summary>
    /// Seeds every canonical test fixture (Books, Audiobooks, Movies, TV, Music, Comics)
    /// into the configured Watch Folders. Mirrors the file-creation logic of
    /// <see cref="SeedLibraryAsync"/> but returns a simple file-count instead of an
    /// HTTP result so the integration-test harness can drive seeding without going
    /// through the HTTP endpoint.
    ///
    /// <para>
    /// Critical: this helper is the single source of truth for test-fixture seeding.
    /// IntegrationTestEndpoints must never maintain its own shadow copy of the seed
    /// arrays — doing so causes silent "NotFound" drift when fixtures are added to
    /// <see cref="DevSeedEndpoints"/> but not mirrored in the harness.
    /// </para>
    /// </summary>
    internal static async Task<int> SeedAllAsync(
        IOptions<IngestionOptions> options,
        IConfigurationLoader configLoader,
        HashSet<string> activeTypes,
        ILogger logger)
    {
        int created = 0;

        // Books (EPUB)
        string? booksDir = ResolveWatchDirectory(configLoader, options, "Books");
        if (activeTypes.Contains("books") && !string.IsNullOrWhiteSpace(booksDir))
        {
            EnsureDirectory(booksDir, logger);
            foreach (SeedBook book in SeedBooks)
            {
                string fileName = $"{SanitizeFileName(book.Title)}.epub";
                string filePath = Path.Combine(booksDir, fileName);
                if (File.Exists(filePath)) continue;
                byte[] bytes = EpubBuilder.Create(
                    book.Title, book.Author, book.Isbn, book.Year, book.Description,
                    book.Publisher, book.Language, book.AdditionalAuthors,
                    book.Series, book.SeriesPosition);
                await File.WriteAllBytesAsync(filePath, bytes);
                created++;
            }
        }

        // Audiobooks (MP3) — share the Books folder so the library prior applies
        if (activeTypes.Contains("audiobooks") && !string.IsNullOrWhiteSpace(booksDir))
        {
            foreach (SeedAudiobook ab in SeedAudiobooks)
            {
                string fileName = $"{SanitizeFileName(ab.Title)} - {SanitizeFileName(ab.Narrator)}.mp3";
                string filePath = Path.Combine(booksDir, fileName);
                if (File.Exists(filePath)) continue;
                byte[] bytes = Mp3Builder.Create(
                    ab.Title, ab.Artist, narrator: ab.Narrator,
                    year: ab.Year, language: ab.Language,
                    series: ab.Series, seriesPosition: ab.SeriesPosition,
                    asin: ab.Asin);
                await File.WriteAllBytesAsync(filePath, bytes);
                created++;
            }
        }

        // Movies + TV (MP4) — SeedVideos carries MediaType = "Movie" or "TV"
        foreach (SeedVideo video in SeedVideos)
        {
            string typeKey = video.MediaType == "TV" ? "tv" : "movies";
            if (!activeTypes.Contains(typeKey)) continue;
            string category = video.MediaType == "TV" ? "TV" : "Movies";
            string? videoDir = ResolveWatchDirectory(configLoader, options, category);
            if (string.IsNullOrWhiteSpace(videoDir)) continue;
            EnsureDirectory(videoDir, logger);

            string fileName;
            string filePath;
            if (!string.IsNullOrWhiteSpace(video.FileNameOverride))
            {
                var rel = video.FileNameOverride.Replace('/', Path.DirectorySeparatorChar);
                filePath = Path.Combine(videoDir, rel);
                fileName = Path.GetFileName(filePath);
                var parentDir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrWhiteSpace(parentDir))
                    EnsureDirectory(parentDir, logger);
            }
            else
            {
                if (video.MediaType == "TV" && video.SeasonNumber is not null && video.EpisodeNumber is not null)
                    fileName = $"{SanitizeFileName(video.Series ?? video.Title)} S{video.SeasonNumber:D2}E{video.EpisodeNumber:D2}.mp4";
                else
                    fileName = $"{SanitizeFileName(video.Title)} ({video.Year}).mp4";
                filePath = Path.Combine(videoDir, fileName);
            }

            if (File.Exists(filePath)) continue;
            byte[] bytes = Mp4Builder.Create(
                video.Title, video.Director, video.Year,
                showName: video.MediaType == "TV" ? video.Series : null,
                seasonNumber: video.SeasonNumber,
                episodeNumber: video.EpisodeNumber);
            await File.WriteAllBytesAsync(filePath, bytes);
            created++;
        }

        // Music (FLAC)
        string? musicDir = ResolveWatchDirectory(configLoader, options, "Music");
        if (activeTypes.Contains("music") && !string.IsNullOrWhiteSpace(musicDir))
        {
            EnsureDirectory(musicDir, logger);
            foreach (SeedMusic track in SeedMusicTracks)
            {
                string fileName = $"{SanitizeFileName(track.Artist)} - {SanitizeFileName(track.Title)}.flac";
                string filePath = Path.Combine(musicDir, fileName);
                if (File.Exists(filePath)) continue;
                byte[] bytes = FlacBuilder.Create(
                    track.Title, track.Artist, track.Album,
                    track.Year, track.Genre, track.TrackNumber);
                await File.WriteAllBytesAsync(filePath, bytes);
                created++;
            }
        }

        // Comics (CBZ)
        string? comicsDir = ResolveWatchDirectory(configLoader, options, "Comics");
        if (activeTypes.Contains("comics") && !string.IsNullOrWhiteSpace(comicsDir))
        {
            EnsureDirectory(comicsDir, logger);
            foreach (SeedComic comic in SeedComics)
            {
                string fileName = $"{SanitizeFileName(comic.Title)}.cbz";
                string filePath = Path.Combine(comicsDir, fileName);
                if (File.Exists(filePath)) continue;
                byte[] bytes = CbzBuilder.Create(
                    comic.Title, comic.Writer, comic.Series, comic.Number,
                    comic.Year, comic.Genre, comic.Summary, comic.Publisher, comic.Penciller);
                await File.WriteAllBytesAsync(filePath, bytes);
                created++;
            }
        }

        logger.LogInformation("[SeedAllAsync] {Count} seed files written across active types {Types}",
            created, string.Join(",", activeTypes));

        // Allow file handles to fully release before the filesystem watcher fires.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        await Task.Delay(3000);

        return created;
    }

    /// <summary>
    /// Returns all seed fixtures as a flat list of expectations.
    /// Called by IntegrationTestEndpoints to build its reconciliation pass without
    /// duplicating the seed data.
    /// </summary>
    public static IReadOnlyList<SeedExpectation> GetAllExpectations()
    {
        var result = new List<SeedExpectation>();

        // Books
        foreach (var b in SeedBooks)
            result.Add(new SeedExpectation(
                Title: b.Title,
                MediaType: "Books",
                Author: b.Author,
                ExpectIdentified: b.ExpectIdentified,
                ExpectedReviewTrigger: b.ExpectedReviewTrigger,
                ExpectedReason: b.ExpectedReason,
                ExpectedProvider: b.ExpectedProvider,
                ExpectedQid: b.ExpectedQid,
                ExpectedCoverArt: b.ExpectedCoverArt));

        // Audiobooks
        foreach (var a in SeedAudiobooks)
            result.Add(new SeedExpectation(
                Title: a.Title,
                MediaType: "Audiobooks",
                Author: a.Artist,
                ExpectIdentified: a.ExpectIdentified,
                ExpectedReviewTrigger: a.ExpectedReviewTrigger,
                ExpectedReason: a.ExpectedReason,
                ExpectedProvider: a.ExpectedProvider,
                ExpectedQid: a.ExpectedQid,
                ExpectedCoverArt: a.ExpectedCoverArt));

        // Videos — split by MediaType field
        foreach (var v in SeedVideos)
        {
            string mt = v.MediaType == "TV" ? "TV" : "Movies";
            result.Add(new SeedExpectation(
                Title: v.Title,
                MediaType: mt,
                Author: v.Director,
                ExpectIdentified: v.ExpectIdentified,
                ExpectedReviewTrigger: v.ExpectedReviewTrigger,
                ExpectedReason: v.ExpectedReason,
                ExpectedProvider: v.ExpectedProvider,
                ExpectedQid: v.ExpectedQid,
                ExpectedCoverArt: v.ExpectedCoverArt));
        }

        // Music
        foreach (var m in SeedMusicTracks)
            result.Add(new SeedExpectation(
                Title: m.Title,
                MediaType: "Music",
                Author: m.Artist,
                ExpectIdentified: m.ExpectIdentified,
                ExpectedReviewTrigger: m.ExpectedReviewTrigger,
                ExpectedReason: m.ExpectedReason,
                ExpectedProvider: m.ExpectedProvider,
                ExpectedQid: m.ExpectedQid,
                ExpectedCoverArt: m.ExpectedCoverArt));

        // Comics
        foreach (var c in SeedComics)
            result.Add(new SeedExpectation(
                Title: c.Title,
                MediaType: "Comics",
                Author: c.Writer,
                ExpectIdentified: c.ExpectIdentified,
                ExpectedReviewTrigger: c.ExpectedReviewTrigger,
                ExpectedReason: c.ExpectedReason,
                ExpectedProvider: c.ExpectedProvider,
                ExpectedQid: c.ExpectedQid,
                ExpectedCoverArt: c.ExpectedCoverArt));

        return result;
    }

    // ── GET /dev/pipeline-status ────────────────────────────────────────────

    private static async Task<IResult> PipelineStatusAsync(
        IIdentityJobRepository jobRepo,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var allStates = Enum.GetValues<IdentityJobState>();
        var counts = new Dictionary<string, int>();
        var nonTerminalDetails = new List<object>();

        foreach (var state in allStates)
        {
            var jobs = await jobRepo.GetByStateAsync(state, 500, ct);
            counts[state.ToString()] = jobs.Count;

            // Show details for non-terminal states (not Completed)
            if (state != IdentityJobState.Completed && jobs.Count > 0)
            {
                foreach (var job in jobs)
                {
                    nonTerminalDetails.Add(new
                    {
                        job.Id,
                        job.EntityId,
                        State = job.State,
                        job.AttemptCount,
                        job.LastError,
                        job.LeaseOwner,
                        job.LeaseExpiresAt,
                        job.ResolvedQid,
                        job.UpdatedAt
                    });
                }
            }
        }

        var total = counts.Values.Sum();
        logger.LogInformation("[PipelineStatus] {Total} total jobs: {Counts}",
            total, string.Join(", ", counts.Where(c => c.Value > 0).Select(c => $"{c.Key}={c.Value}")));

        return Results.Ok(new
        {
            total,
            counts,
            non_terminal_details = nonTerminalDetails
        });
    }
}
