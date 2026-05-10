// â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
// GenerateTestEpubs â€” Creates a full test library spanning the configured
// watch roots and exercising every major ingestion edge case.
//
// Scenario groups:
//   EPUBs  1â€“ 8  : Confidence gates, Collection? grouping, person records, pseudonyms
//   EPUB   9     : Corrupt file â†’ quarantine
//   EPUB  10     : Duplicate hash â†’ skip
//   M4Bs 11â€“16   : Cross-format Collection? link, narrators, orphanage, pseudonym
//   M4Bs 17â€“18   : Ingestion hinting â€” hp-series/ folder (sibling files)
//   M4Bs 19â€“20   : Ingestion hinting â€” expanse-audio/ folder (sibling files)
//   EPUBs 21â€“23  : Pseudonym (individual), no-ISBN search, title mismatch
//   EPUBs 24â€“26  : Foreign language metadata (Russian, Spanish, French)
//   EPUB  27     : Same-author different-work
//   EPUB  28     : Same-title different-edition disambiguation
//   EPUBs 29â€“30  : Multi-author works
//
// Usage:
//   dotnet run --project tools/GenerateTestEpubs [watch-root-or-books-directory] [--clean]
//
// Default output : C:\temp\tuvima-watch
// --clean        : Wipes the watch root before generating
// â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

// â”€â”€ Args â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

bool clean = args.Any(a => a.Equals("--clean", StringComparison.OrdinalIgnoreCase));
var requestedOutputDir = args.FirstOrDefault(a => !a.StartsWith("--")) ?? @"C:\temp\tuvima-watch";
var normalizedRequestedOutputDir = Path.GetFullPath(requestedOutputDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
var watchRoot = string.Equals(Path.GetFileName(normalizedRequestedOutputDir), "books", StringComparison.OrdinalIgnoreCase)
    ? Directory.GetParent(normalizedRequestedOutputDir)?.FullName ?? normalizedRequestedOutputDir
    : normalizedRequestedOutputDir;
var booksDir = Path.Combine(watchRoot, "books");
var moviesDir = Path.Combine(watchRoot, "movies");
var tvDir = Path.Combine(watchRoot, "tv");
var musicDir = Path.Combine(watchRoot, "music");
var comicsDir = Path.Combine(watchRoot, "comics");
var generalDir = watchRoot;
var tempDir   = Path.Combine(Path.GetTempPath(), "tuvima-test-gen");
var ffmpegPath = FindFfmpeg();

// â”€â”€ Clean â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

if (clean && Directory.Exists(watchRoot))
{
    Console.WriteLine($"  --clean  Wiping {watchRoot}");
    Directory.Delete(watchRoot, recursive: true);
    Console.WriteLine();
}

Directory.CreateDirectory(booksDir);
Directory.CreateDirectory(moviesDir);
Directory.CreateDirectory(tvDir);
Directory.CreateDirectory(musicDir);
Directory.CreateDirectory(comicsDir);
Directory.CreateDirectory(generalDir);
Directory.CreateDirectory(tempDir);

Console.WriteLine($"Watch root       : {watchRoot}");
Console.WriteLine($"Books/Audiobooks: {booksDir}");
Console.WriteLine($"Movies           : {moviesDir}");
Console.WriteLine($"TV               : {tvDir}");
Console.WriteLine($"Music            : {musicDir}");
Console.WriteLine($"Comics           : {comicsDir}");
Console.WriteLine($"FFmpeg           : {ffmpegPath ?? "NOT FOUND â€” M4B files will be skipped"}");
Console.WriteLine();

// â”€â”€ EPUB definitions â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
//
// Scenarios 1-8. Output to the configured Books watch folder.
//
var epubs = new EpubSpec[]
{
    // Scenario 1 â€” Fully tagged: title, author, ISBN, series, embedded cover.
    //   Expected: auto-organized into library; "Dune" Collection? created; cover from file.
    new("dune.epub",
        "Dune",
        Author: "Frank Herbert",         SecondAuthor: null,
        Isbn: "9780441172719",           Year: "1965",
        Publisher: "Ace Books",
        Description: "A science fiction masterpiece set on the desert planet Arrakis.",
        Series: "Dune Chronicles",       SeriesPosition: "1",
        Language: "en",                  IncludeCover: true,
        CoverHex: "#B5651D"),

    // Scenario 2 â€” Rich metadata, no embedded cover art.
    //   Expected: Hub created; cover fetched from provider (Apple Books / Open Library).
    new("neuromancer.epub",
        "Neuromancer",
        Author: "William Gibson",        SecondAuthor: null,
        Isbn: "9780441569595",           Year: "1984",
        Publisher: "Ace Books",
        Description: "Case was the sharpest data-thief in the matrix.",
        Series: null,                    SeriesPosition: null,
        Language: "en",                  IncludeCover: false,
        CoverHex: "#2C6B3E"),

    // Scenario 3 â€” Author tagged as "Asimov, Isaac" (Last, First reversed format).
    //   Expected: author conflict flagged; two Person records may be created
    //   (one per format variant until Wikidata normalises to canonical name).
    new("foundation.epub",
        "Foundation",
        Author: "Asimov, Isaac",          SecondAuthor: null,
        Isbn: "9780553293357",            Year: "1951",
        Publisher: "Gnome Press",
        Description: "The Foundation series is set in the far future.",
        Series: "Foundation",            SeriesPosition: "1",
        Language: "en",                  IncludeCover: false,
        CoverHex: "#1A237E"),

    // Scenario 4 â€” Series + series_pos present in OPF metadata.
    //   Expected: `series` and `series_pos` canonical values correctly set on Work.
    new("the-name-of-the-wind.epub",
        "The Name of the Wind",
        Author: "Patrick Rothfuss",      SecondAuthor: null,
        Isbn: "9780756404079",           Year: "2007",
        Publisher: "DAW Books",
        Description: "I have stolen princesses back from sleeping barrow kings.",
        Series: "The Kingkiller Chronicle",  SeriesPosition: "1",
        Language: "en",                  IncludeCover: true,
        CoverHex: "#8B4513"),

    // Scenario 5 â€” Author is a collective pen name: "James S.A. Corey" resolves
    //   to two real people (Daniel Abraham + Ty Franck) via Wikidata P1773.
    //   Expected: collective pseudonym link; two real-author Person records.
    new("leviathan-wakes.epub",
        "Leviathan Wakes",
        Author: "James S.A. Corey",      SecondAuthor: null,
        Isbn: "9780316129084",           Year: "2011",
        Publisher: "Orbit",
        Description: "Humanity has colonized the solar system.",
        Series: "The Expanse",           SeriesPosition: "1",
        Language: "en",                  IncludeCover: true,
        CoverHex: "#1A237E"),

    // Scenario 6 â€” PSEUDONYM: "Richard Bachman" is Stephen King's pen name
    //   (Wikidata Q3324300 â†’ P1773 â†’ Q39829).
    //   Expected: Person record for Bachman linked to King via pseudonym.
    new("the-running-man.epub",
        "The Running Man",
        Author: "Richard Bachman",       SecondAuthor: null,
        Isbn: "9780451197962",           Year: "1982",
        Publisher: "Signet",
        Description: "A desperate man enters a deadly game show in a dystopian future.",
        Series: null,                    SeriesPosition: null,
        Language: "en",                  IncludeCover: true,
        CoverHex: "#8B0000"),

    // Scenario 7 â€” PSEUDONYM: "Robert Galbraith" is J.K. Rowling's pen name
    //   (Wikidata Q16308388 â†’ P1773 â†’ Q34660).
    //   Expected: Person record for Galbraith linked to Rowling via pseudonym.
    new("the-cuckoos-calling.epub",
        "The Cuckoo's Calling",
        Author: "Robert Galbraith",      SecondAuthor: null,
        Isbn: "9780316206846",           Year: "2013",
        Publisher: "Mulholland Books",
        Description: "A brilliant mystery novel featuring private detective Cormoran Strike.",
        Series: "Cormoran Strike",       SeriesPosition: "1",
        Language: "en",                  IncludeCover: true,
        CoverHex: "#2F4F4F"),

    // Scenario 8 â€” Filename only: OPF contains no usable metadata (all fields empty).
    //   Expected: overall confidence < 0.40 â†’ file moved to .orphans/; review queue entry.
    new("phantom-signal-filename-only.epub",
        Title: "",                        Author: "",
        SecondAuthor: null,
        Isbn: "",                         Year: "",
        Publisher: "",                    Description: "",
        Series: null,                    SeriesPosition: null,
        Language: "en",                  IncludeCover: false,
        CoverHex: "#212121"),

    // Scenario 21 â€” INDIVIDUAL PEN NAME: "J.D. Robb" is Nora Roberts's crime-fiction pen name
    //   (Wikidata Q4808063 is the pseudonym entity, P1773 â†’ Q231811 Nora Roberts).
    //   Unlike the collective pseudonym (James S.A. Corey), this is one real author behind one name.
    //   Expected: author audit finds pseudonym entity, emits "J.D. Robb" at confidence 1.0.
    //   The "author" canonical value should stay "J.D. Robb" (the published name on the cover).
    new("naked-in-death.epub",
        "Naked in Death",
        Author: "J.D. Robb",             SecondAuthor: null,
        Isbn: "9780425148990",           Year: "1995",
        Publisher: "Berkley Books",
        Description: "Detective Eve Dallas investigates a futuristic murder in New York.",
        Series: "In Death",              SeriesPosition: "1",
        Language: "en",                  IncludeCover: true,
        CoverHex: "#1B1B2F"),

    // Scenario 22 â€” NO ISBN: forces Tier 2 structured SPARQL title+author search.
    //   "Frankenstein" by Mary Shelley is a well-known work with a clear Wikidata entry
    //   (Q192676) but an 1818 publication date means no ISBN exists in most embedded metadata.
    //   Expected: Tier 2 search resolves Q192676; no bridge lookup used.
    new("frankenstein.epub",
        "Frankenstein",
        Author: "Mary Shelley",          SecondAuthor: null,
        Isbn: "",                        Year: "1818",
        Publisher: "Lackington, Hughes, Harding, Mavor & Jones",
        Description: "The modern Prometheus creates life and faces the consequences.",
        Series: null,                    SeriesPosition: null,
        Language: "en",                  IncludeCover: false,
        CoverHex: "#1A1A1A"),

    // Scenario 23 â€” TITLE MISMATCH / DISAMBIGUATION: EPUB title is "1984" but Wikidata
    //   calls the work "Nineteen Eighty-Four" (Q208592). No ISBN â€” forces title search.
    //   "1984" as a search term returns many candidates (year references, other works).
    //   Expected: Tier 2 search with author cross-check finds Q208592; no ISBN bridge used.
    //   Verifies that the search service matches "1984" â†” "Nineteen Eighty-Four" correctly.
    new("nineteen-eighty-four.epub",
        "1984",
        Author: "George Orwell",         SecondAuthor: null,
        Isbn: "",                        Year: "1949",
        Publisher: "Secker & Warburg",
        Description: "Big Brother is watching you. A dystopian novel of totalitarian surveillance.",
        Series: null,                    SeriesPosition: null,
        Language: "en",                  IncludeCover: true,
        CoverHex: "#0A0A0A"),

    // â”€â”€ Foreign Language Metadata â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    // Scenario 24 â€” FOREIGN LANGUAGE: Russian title and author in Cyrillic.
    //   <dc:language>ru</dc:language> triggers LanguageMismatch review.
    //   Expected: routed to review queue with "File metadata is in Russian" detail.
    new("war-and-peace.epub",
        "Ð’Ð¾Ð¹Ð½Ð° Ð¸ Ð¼Ð¸Ñ€",
        Author: "Ð›ÐµÐ² Ð¢Ð¾Ð»ÑÑ‚Ð¾Ð¹",              SecondAuthor: null,
        Isbn: "9780140447934",               Year: "1869",
        Publisher: "Penguin Classics",
        Description: "Ð­Ð¿Ð¸Ñ‡ÐµÑÐºÐ¸Ð¹ Ñ€Ð¾Ð¼Ð°Ð½ Ð¾ Ñ€ÑƒÑÑÐºÐ¾Ð¼ Ð¾Ð±Ñ‰ÐµÑÑ‚Ð²Ðµ Ð²Ð¾ Ð²Ñ€ÐµÐ¼Ñ Ð½Ð°Ð¿Ð¾Ð»ÐµÐ¾Ð½Ð¾Ð²ÑÐºÐ¸Ñ… Ð²Ð¾Ð¹Ð½.",
        Series: null,                        SeriesPosition: null,
        Language: "ru",                      IncludeCover: true,
        CoverHex: "#8D6E63"),

    // Scenario 25 â€” FOREIGN LANGUAGE: Spanish title.
    //   <dc:language>es</dc:language> triggers LanguageMismatch review.
    //   Expected: routed to review queue with "File metadata is in Spanish" detail.
    new("don-quijote.epub",
        "Don Quijote de la Mancha",
        Author: "Miguel de Cervantes",       SecondAuthor: null,
        Isbn: "9788420412146",               Year: "1605",
        Publisher: "Real Academia EspaÃ±ola",
        Description: "La historia del ingenioso hidalgo Don Quijote de la Mancha.",
        Series: null,                        SeriesPosition: null,
        Language: "es",                      IncludeCover: true,
        CoverHex: "#D84315"),

    // Scenario 26 â€” FOREIGN LANGUAGE: French title.
    //   <dc:language>fr</dc:language> triggers LanguageMismatch review.
    //   Expected: routed to review queue with "File metadata is in French" detail.
    new("les-trois-mousquetaires.epub",
        "Les Trois Mousquetaires",
        Author: "Alexandre Dumas",           SecondAuthor: null,
        Isbn: "9782070409228",               Year: "1844",
        Publisher: "Gallimard",
        Description: "Les aventures de d'Artagnan et des trois mousquetaires.",
        Series: null,                        SeriesPosition: null,
        Language: "fr",                      IncludeCover: true,
        CoverHex: "#1565C0"),

    // â”€â”€ Same-Author Different-Work â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    // Scenario 27 â€” SAME AUTHOR: George Orwell also wrote #23 (1984).
    //   Expected: separate Work, same Person record as scenario 23.
    new("animal-farm.epub",
        "Animal Farm",
        Author: "George Orwell",             SecondAuthor: null,
        Isbn: "9780451526342",               Year: "1945",
        Publisher: "Secker & Warburg",
        Description: "A satirical allegory of Soviet totalitarianism.",
        Series: null,                        SeriesPosition: null,
        Language: "en",                      IncludeCover: true,
        CoverHex: "#33691E"),

    // â”€â”€ Title Disambiguation â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    // Scenario 28 â€” SAME TITLE DIFFERENT EDITION: "Foundation" with different ISBN than #3.
    //   Expected: different Edition under same Work; ISBN mismatch may trigger review.
    new("foundation-del-rey.epub",
        "Foundation",
        Author: "Isaac Asimov",              SecondAuthor: null,
        Isbn: "9780553803716",               Year: "1951",
        Publisher: "Del Rey",
        Description: "The first novel in Asimov's Foundation series, Del Rey edition.",
        Series: "Foundation",                SeriesPosition: "1",
        Language: "en",                      IncludeCover: false,
        CoverHex: "#283593"),

    // â”€â”€ Multi-Author â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    // Scenario 29 â€” MULTI-AUTHOR: Two <dc:creator> entries in OPF.
    //   Expected: two Person records created (Pratchett + Gaiman), both linked.
    new("good-omens.epub",
        "Good Omens",
        Author: "Terry Pratchett",           SecondAuthor: "Neil Gaiman",
        Isbn: "9780060853983",               Year: "1990",
        Publisher: "Workman Publishing",
        Description: "The Nice and Accurate Prophecies of Agnes Nutter, Witch.",
        Series: null,                        SeriesPosition: null,
        Language: "en",                      IncludeCover: true,
        CoverHex: "#FFD54F"),

    // Scenario 30 â€” MULTI-AUTHOR: Stephen King + Peter Straub collaboration.
    //   Expected: two Person records; King already exists from #6 (Bachman pseudonym).
    new("the-talisman.epub",
        "The Talisman",
        Author: "Stephen King",              SecondAuthor: "Peter Straub",
        Isbn: "9781501192272",               Year: "1984",
        Publisher: "Viking Press",
        Description: "A boy's epic quest across two parallel worlds.",
        Series: null,                        SeriesPosition: null,
        Language: "en",                      IncludeCover: true,
        CoverHex: "#4A148C"),
};

// â”€â”€ Generate EPUBs 1â€“8, 21â€“30 â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

int total = 0, failed = 0;
var manifest = new List<ManifestEntry>();
var generatedFiles = new List<(string Temp, string Final)>();
var expectedPeople = new[]
{
    new ExpectedPersonEntry(
        "Frank Herbert",
        ExpectedWikidataQid: "Q7934",
        MinimumOwnedCredits: 4,
        MinimumMediaItems: 4,
        ExpectedMediaTypes: ["Books", "Audiobooks"],
        ExpectedTitles: ["Dune", "Dune Messiah", "Children of Dune"],
        RequireBiography: true,
        RequireHeadshot: true,
        Note: "Author appears across Dune EPUBs and the Dune audiobook."),
    new ExpectedPersonEntry(
        "George Orwell",
        ExpectedWikidataQid: "Q3335",
        MinimumOwnedCredits: 2,
        MinimumMediaItems: 2,
        ExpectedMediaTypes: ["Books"],
        ExpectedTitles: ["1984", "Animal Farm"],
        RequireBiography: true,
        RequireHeadshot: true,
        Note: "Same author, different works."),
    new ExpectedPersonEntry(
        "J.K. Rowling",
        ExpectedWikidataQid: "Q34660",
        MinimumOwnedCredits: 2,
        MinimumMediaItems: 2,
        ExpectedMediaTypes: ["Audiobooks"],
        ExpectedTitles: ["Harry Potter and the Philosopher's Stone", "Harry Potter and the Chamber of Secrets"],
        RequireBiography: true,
        RequireHeadshot: true,
        Note: "Same author across sibling audiobook files."),
    new ExpectedPersonEntry(
        "James S.A. Corey",
        ExpectedWikidataQid: "Q6142591",
        MinimumOwnedCredits: 2,
        MinimumMediaItems: 2,
        ExpectedMediaTypes: ["Audiobooks"],
        ExpectedTitles: ["Leviathan Wakes", "Caliban's War"],
        RequireBiography: true,
        RequireHeadshot: false,
        Note: "Collective pen name appears across EPUB and audiobook fixtures."),
    new ExpectedPersonEntry(
        "Jefferson Mays",
        ExpectedWikidataQid: null,
        MinimumOwnedCredits: 2,
        MinimumMediaItems: 2,
        ExpectedMediaTypes: ["Audiobooks"],
        ExpectedTitles: ["Leviathan Wakes", "Caliban's War"],
        RequireBiography: true,
        RequireHeadshot: true,
        Note: "Narrator appears across multiple Expanse audiobooks."),
};

Console.WriteLine($"â”â”â” EPUBs (scenarios 1â€“8, 21â€“30) â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”");
string? duneEpubPath = null;

// Scenarios 1â€“8 are indices 0â€“7; scenarios 21â€“23 are indices 8â€“10; scenarios 24â€“30 are indices 11â€“17.
// Map index â†’ scenario number for the manifest and console output.
static int EpubScenarioNum(int index) => index switch
{
    < 8 => index + 1,         // 0â€“7  â†’ scenarios 1â€“8
    < 11 => 21 + (index - 8), // 8â€“10 â†’ scenarios 21â€“23
    _ => 24 + (index - 11),   // 11â€“17 â†’ scenarios 24â€“30
};

for (int i = 0; i < epubs.Length; i++)
{
    var spec    = epubs[i];
    var outPath = Path.Combine(tempDir, spec.FileName);
    var finalPath = Path.Combine(booksDir, spec.FileName);
    var num     = EpubScenarioNum(i);
    try
    {
        byte[]? cover = null;
        if (spec.IncludeCover && ffmpegPath is not null)
            cover = GeneratePng(ffmpegPath, tempDir, spec.CoverHex, 400, 600);

        CreateEpub(outPath, spec, cover);
        if (spec.FileName == "dune.epub") duneEpubPath = outPath;
        generatedFiles.Add((outPath, finalPath));

        var label = $"[{num,2}] {spec.FileName,-46}";
        Console.WriteLine($"  âœ“  {label} {(cover is not null ? "[cover]" : "[no cover]")}");
        manifest.Add(new(num, spec.FileName, "epub", $"Scenario {num}"));
        total++;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  âœ—  [{num,2}] {spec.FileName}: {ex.Message}");
        failed++;
    }
}

// â”€â”€ Scenario 9 â€” Corrupt EPUB â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
//   File has .epub extension but contains garbage bytes (not a valid ZIP).
//   Expected: processor returns IsCorrupt=true; no asset in DB; MediaFailed activity.
{
    const int num = 9;
    var outPath = Path.Combine(tempDir, "corrupt-epub.epub");
    var finalPath = Path.Combine(booksDir, "corrupt-epub.epub");
    try
    {
        // Valid ZIP magic is PK\x03\x04; we write garbage that will fail ZIP parsing.
        var garbage = new byte[512];
        new Random(42).NextBytes(garbage);
        garbage[0] = 0xFF; garbage[1] = 0xFE;   // deliberate non-ZIP header
        File.WriteAllBytes(outPath, garbage);
        generatedFiles.Add((outPath, finalPath));

        Console.WriteLine($"  âœ“  [{num,2}] {"corrupt-epub.epub",-46} [corrupt bytes â€” not a valid ZIP]");
        manifest.Add(new(num, "corrupt-epub.epub", "epub", "Scenario 9 â€” corrupt"));
        total++;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  âœ—  [{num,2}] corrupt-epub.epub: {ex.Message}");
        failed++;
    }
}

// â”€â”€ Scenario 10 â€” Duplicate (byte-identical copy of dune.epub) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
//   Expected: hash check catches duplicate before processor runs; DuplicateSkipped
//   activity logged; no second asset created in DB.
{
    const int num = 10;
    var outPath = Path.Combine(tempDir, "dune-duplicate.epub");
    var finalPath = Path.Combine(booksDir, "dune-duplicate.epub");
    try
    {
        if (duneEpubPath is not null && File.Exists(duneEpubPath))
        {
            File.Copy(duneEpubPath, outPath, overwrite: true);
            generatedFiles.Add((outPath, finalPath));
            Console.WriteLine($"  âœ“  [{num,2}] {"dune-duplicate.epub",-46} [byte-identical copy of dune.epub]");
            manifest.Add(new(num, "dune-duplicate.epub", "epub", "Scenario 10 â€” duplicate"));
            total++;
        }
        else
        {
            Console.WriteLine($"  âœ—  [{num,2}] dune-duplicate.epub: dune.epub was not generated â€” cannot copy");
            failed++;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  âœ—  [{num,2}] dune-duplicate.epub: {ex.Message}");
        failed++;
    }
}

// â”€â”€ Extra linked book-series fixtures â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
// Scenarios 31-32 expand the Dune book series so collection ordering can be
// tested across several books, while scenario 11 remains the matching audiobook
// for scenario 1.
var extraBookSeries = new (int Scenario, EpubSpec Spec)[]
{
    new(31, new EpubSpec("dune-messiah.epub",
        "Dune Messiah",
        Author: "Frank Herbert", SecondAuthor: null,
        Isbn: "9780593098233", Year: "1969",
        Publisher: "Ace Books",
        Description: "The second novel in the Dune Chronicles.",
        Series: "Dune Chronicles", SeriesPosition: "2",
        Language: "en", IncludeCover: true,
        CoverHex: "#7A4A24")),

    new(32, new EpubSpec("children-of-dune.epub",
        "Children of Dune",
        Author: "Frank Herbert", SecondAuthor: null,
        Isbn: "9780441104024", Year: "1976",
        Publisher: "Ace Books",
        Description: "The third novel in the Dune Chronicles.",
        Series: "Dune Chronicles", SeriesPosition: "3",
        Language: "en", IncludeCover: true,
        CoverHex: "#8A6F2A")),
};

Console.WriteLine();
Console.WriteLine($"â”â”â” Extra EPUB series fixtures (scenarios 31-32) â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”");
foreach (var (num, spec) in extraBookSeries)
{
    var outPath = Path.Combine(tempDir, spec.FileName);
    var finalPath = Path.Combine(booksDir, spec.FileName);
    try
    {
        byte[]? cover = null;
        if (spec.IncludeCover && ffmpegPath is not null)
            cover = GeneratePng(ffmpegPath, tempDir, spec.CoverHex, 400, 600);

        CreateEpub(outPath, spec, cover);
        generatedFiles.Add((outPath, finalPath));
        Console.WriteLine($"  âœ“  [{num,2}] {spec.FileName,-46} {(cover is not null ? "[cover]" : "[no cover]")}");
        manifest.Add(new(num, spec.FileName, "epub", $"Scenario {num} â€” Dune book series"));
        total++;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  âœ—  [{num,2}] {spec.FileName}: {ex.Message}");
        failed++;
    }
}
// â”€â”€ M4B definitions â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
//
// Scenarios 11-16 output to the configured Books/Audiobooks watch folder.
// Scenarios 17â€“18 output to hp-series/ subfolder  (ingestion hinting).
// Scenarios 19â€“20 output to expanse-audio/ subfolder (ingestion hinting).
//
// Audiobook covers MUST be square (1:1 aspect ratio).

var m4bsFlat = new M4bSpec[]
{
    // Scenario 11 â€” Same title, author, and series as dune.epub (#1).
    //   Expected: joins the existing Dune Collection? â€” no new Collection created.
    new("dune-audiobook.m4b",
        Title: "Dune",
        Artist: "Frank Herbert",         AlbumArtist: "Frank Herbert",
        Album: "Dune",                   Narrator: "Scott Brick",
        Year: "1965",                    Genre: "Science Fiction",
        Comment: "Narrated by Scott Brick",
        TrackNum: "1",
        Series: "Dune Chronicles",       SeriesPos: "1",
        IncludeCover: false,             CoverHex: "#B5651D"),

    // Scenario 12 â€” Narrator credited in ID3 comment tag.
    //   Expected: Narrator Person record created for Stephen Fry.
    new("hitchhikers-guide.m4b",
        Title: "The Hitchhiker's Guide to the Galaxy",
        Artist: "Douglas Adams",         AlbumArtist: "Douglas Adams",
        Album: "Hitchhiker's Guide to the Galaxy",
        Narrator: "Stephen Fry",
        Year: "1979",                    Genre: "Science Fiction",
        Comment: "Narrated by Stephen Fry",
        TrackNum: "1",
        Series: "The Hitchhiker's Guide to the Galaxy",
        SeriesPos: "1",
        IncludeCover: true,              CoverHex: "#0097A7"),

    // Scenario 13 â€” Narrator field contains two names joined by " and ".
    //   Expected: two separate Narrator Person records (one per name).
    new("wool-omnibus.m4b",
        Title: "Wool",
        Artist: "Hugh Howey",            AlbumArtist: "Hugh Howey",
        Album: "Wool Omnibus",           Narrator: "Amanda Donahoe and Tim Gerard Reynolds",
        Year: "2012",                    Genre: "Science Fiction",
        Comment: "Narrated by Amanda Donahoe and Tim Gerard Reynolds",
        TrackNum: "1",
        Series: "Silo",                  SeriesPos: "1",
        IncludeCover: true,              CoverHex: "#4E342E"),

    // Scenario 14 â€” Audiobook with series, no embedded cover.
    //   Expected: Hub created; cover fetched from provider (Audnexus / Apple Books).
    new("enders-game.m4b",
        Title: "Ender's Game",
        Artist: "Orson Scott Card",      AlbumArtist: "Orson Scott Card",
        Album: "Ender's Game",           Narrator: "Stefan Rudnicki",
        Year: "1985",                    Genre: "Science Fiction",
        Comment: "Narrated by Stefan Rudnicki",
        TrackNum: "1",
        Series: "Ender's Saga",          SeriesPos: "1",
        IncludeCover: false,             CoverHex: "#006064"),

    // Scenario 15 â€” No ID3 tags at all (filename-only audiobook).
    //   Expected: overall confidence < 0.40 â†’ .orphans/ quarantine; review entry.
    new("echoes-filename-only.m4b",
        Title: "",                        Artist: "",
        AlbumArtist: "",                 Album: "",
        Narrator: "",                    Year: "",
        Genre: "",                        Comment: "",
        TrackNum: "",
        Series: null,                    SeriesPos: null,
        IncludeCover: false,             CoverHex: "#212121"),

    // Scenario 16 â€” PSEUDONYM: "Iain Banks" has pen name "Iain M. Banks"
    //   (Wikidata Q14469 â†’ P742 â†’ Q214540).
    //   Expected: pseudonym link discovered; both Person records linked.
    new("the-wasp-factory.m4b",
        Title: "The Wasp Factory",
        Artist: "Iain Banks",            AlbumArtist: "Iain Banks",
        Album: "The Wasp Factory",       Narrator: "Peter Kenny",
        Year: "1984",                    Genre: "Fiction",
        Comment: "Narrated by Peter Kenny",
        TrackNum: "1",
        Series: null,                    SeriesPos: null,
        IncludeCover: true,              CoverHex: "#4A0E0E"),
};

// â”€â”€ Ingestion hinting â€” hp-series/ subdirectory â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
//
// Both files go into the same source subfolder.  The Engine primes a FolderHint
// when the first file is ingested, then applies it to the second â€” skipping a
// redundant Stage 1 SPARQL query and pre-assigning the second file to the same Collection.

var hpSubdir = Path.Combine(booksDir, "hp-series");
var tempHpSubdir = Path.Combine(tempDir, "hp-series");
Directory.CreateDirectory(tempHpSubdir);
var m4bsHpSeries = new M4bSpec[]
{
    // Scenario 17 â€” Harry Potter #1. First file in hp-series/ folder.
    //   Expected: full three-stage pipeline; ingestion hint primed with HP Collection? ID + QID.
    new("harry-potter-philosophers-stone.m4b",
        Title: "Harry Potter and the Philosopher's Stone",
        Artist: "J.K. Rowling",          AlbumArtist: "J.K. Rowling",
        Album: "Harry Potter",           Narrator: "Jim Dale",
        Year: "1997",                    Genre: "Fantasy",
        Comment: "Narrated by Jim Dale",
        TrackNum: "1",
        Series: "Harry Potter",          SeriesPos: "1",
        IncludeCover: true,              CoverHex: "#7B1FA2"),

    // Scenario 18 â€” Harry Potter #2. Sibling in hp-series/ folder.
    //   Expected: FolderHint applied from #17; Collection? pre-assigned; Stage 1 SPARQL skipped.
    new("harry-potter-chamber-of-secrets.m4b",
        Title: "Harry Potter and the Chamber of Secrets",
        Artist: "J.K. Rowling",          AlbumArtist: "J.K. Rowling",
        Album: "Harry Potter",           Narrator: "Jim Dale",
        Year: "1998",                    Genre: "Fantasy",
        Comment: "Narrated by Jim Dale",
        TrackNum: "2",
        Series: "Harry Potter",          SeriesPos: "2",
        IncludeCover: true,              CoverHex: "#558B2F"),
};

// â”€â”€ Ingestion hinting â€” expanse-audio/ subdirectory â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

var expanseSubdir = Path.Combine(booksDir, "expanse-audio");
var tempExpanseSubdir = Path.Combine(tempDir, "expanse-audio");
Directory.CreateDirectory(tempExpanseSubdir);
var m4bsExpanse = new M4bSpec[]
{
    // Scenario 19 â€” The Expanse #1. First file in expanse-audio/ folder.
    //   Expected: full pipeline; hint primed with Expanse Collection? ID + bridge IDs.
    new("leviathan-wakes-audio.m4b",
        Title: "Leviathan Wakes",
        Artist: "James S.A. Corey",      AlbumArtist: "James S.A. Corey",
        Album: "The Expanse",            Narrator: "Jefferson Mays",
        Year: "2011",                    Genre: "Science Fiction",
        Comment: "Narrated by Jefferson Mays",
        TrackNum: "1",
        Series: "The Expanse",           SeriesPos: "1",
        IncludeCover: true,              CoverHex: "#0D47A1"),

    // Scenario 20 â€” The Expanse #2. Sibling in expanse-audio/ folder.
    //   Expected: FolderHint applied from #19; same Collection; Stage 1 SPARQL skipped.
    new("calibans-war-audio.m4b",
        Title: "Caliban's War",
        Artist: "James S.A. Corey",      AlbumArtist: "James S.A. Corey",
        Album: "The Expanse",            Narrator: "Jefferson Mays",
        Year: "2012",                    Genre: "Science Fiction",
        Comment: "Narrated by Jefferson Mays",
        TrackNum: "2",
        Series: "The Expanse",           SeriesPos: "2",
        IncludeCover: true,              CoverHex: "#1565C0"),
};

// â”€â”€ Generate M4Bs â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

Console.WriteLine();
Console.WriteLine($"â”â”â” M4Bs flat (scenarios 11â€“16) â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”");

if (ffmpegPath is null)
{
    Console.WriteLine("  âœ—  FFmpeg not found â€” cannot create M4B files.");
    Console.WriteLine("     Run: powershell -ExecutionPolicy Bypass -File tools/Download-FFmpeg.ps1");
    failed += m4bsFlat.Length + m4bsHpSeries.Length + m4bsExpanse.Length;
}
else
{
    foreach (var (spec, idx) in m4bsFlat.Select((s, i) => (s, i)))
    {
        var num      = 11 + idx;
        var outPath  = Path.Combine(tempDir, spec.FileName);
        var finalPath = Path.Combine(booksDir, spec.FileName);
        try
        {
            byte[]? cover = spec.IncludeCover ? GeneratePng(ffmpegPath, tempDir, spec.CoverHex, 400, 400) : null;
            CreateM4b(ffmpegPath, tempDir, outPath, spec, cover);
            generatedFiles.Add((outPath, finalPath));
            Console.WriteLine($"  âœ“  [{num,2}] {spec.FileName,-46} {(cover is not null ? "[sq cover]" : "[no cover]")}");
            manifest.Add(new(num, spec.FileName, "m4b", $"Scenario {num}"));
            total++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  âœ—  [{num,2}] {spec.FileName}: {ex.Message}");
            failed++;
        }
    }

    Console.WriteLine();
    Console.WriteLine($"â”â”â” M4Bs hp-series/ (scenarios 17â€“18, ingestion hinting) â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”");
    Directory.CreateDirectory(hpSubdir);

    foreach (var (spec, idx) in m4bsHpSeries.Select((s, i) => (s, i)))
    {
        var num      = 17 + idx;
        var outPath  = Path.Combine(tempHpSubdir, spec.FileName);
        var finalPath = Path.Combine(hpSubdir, spec.FileName);
        try
        {
            byte[]? cover = spec.IncludeCover ? GeneratePng(ffmpegPath, tempDir, spec.CoverHex, 400, 400) : null;
            CreateM4b(ffmpegPath, tempDir, outPath, spec, cover);
            generatedFiles.Add((outPath, finalPath));
            Console.WriteLine($"  âœ“  [{num,2}] hp-series/{spec.FileName,-38} {(cover is not null ? "[sq cover]" : "[no cover]")}");
            manifest.Add(new(num, $"hp-series/{spec.FileName}", "m4b", $"Scenario {num}"));
            total++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  âœ—  [{num,2}] hp-series/{spec.FileName}: {ex.Message}");
            failed++;
        }
    }

    Console.WriteLine();
    Console.WriteLine($"â”â”â” M4Bs expanse-audio/ (scenarios 19â€“20, ingestion hinting) â”â”â”â”â”â”â”â”â”â”â”â”â”â”");
    Directory.CreateDirectory(expanseSubdir);

    foreach (var (spec, idx) in m4bsExpanse.Select((s, i) => (s, i)))
    {
        var num      = 19 + idx;
        var outPath  = Path.Combine(tempExpanseSubdir, spec.FileName);
        var finalPath = Path.Combine(expanseSubdir, spec.FileName);
        try
        {
            byte[]? cover = spec.IncludeCover ? GeneratePng(ffmpegPath, tempDir, spec.CoverHex, 400, 400) : null;
            CreateM4b(ffmpegPath, tempDir, outPath, spec, cover);
            generatedFiles.Add((outPath, finalPath));
            Console.WriteLine($"  âœ“  [{num,2}] expanse-audio/{spec.FileName,-34} {(cover is not null ? "[sq cover]" : "[no cover]")}");
            manifest.Add(new(num, $"expanse-audio/{spec.FileName}", "m4b", $"Scenario {num}"));
            total++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  âœ—  [{num,2}] expanse-audio/{spec.FileName}: {ex.Message}");
            failed++;
        }
    }
}

// â”€â”€ Movie-series fixtures â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
// Scenarios 33-37 add real movie-series shapes with IMDb bridge IDs in the
// path. The video processor reads filename/year, and OrganizationHintParser
// seeds the bridge identifiers for provider matching.
var movieSeries = new VideoSpec[]
{
    new(33, "dune-films", "Dune Part One (2021) {imdb-tt1160419}.mp4", "Dune: Part One", "2021", "Science Fiction", "#6D4C41"),
    new(34, "dune-films", "Dune Part Two (2024) {imdb-tt15239678}.mp4", "Dune: Part Two", "2024", "Science Fiction", "#A66A2E"),
    new(35, "middle-earth", "The Lord of the Rings The Fellowship of the Ring (2001) {imdb-tt0120737}.mp4", "The Lord of the Rings: The Fellowship of the Ring", "2001", "Fantasy", "#2E5E3F"),
    new(36, "middle-earth", "The Lord of the Rings The Two Towers (2002) {imdb-tt0167261}.mp4", "The Lord of the Rings: The Two Towers", "2002", "Fantasy", "#455A64"),
    new(37, "middle-earth", "The Lord of the Rings The Return of the King (2003) {imdb-tt0167260}.mp4", "The Lord of the Rings: The Return of the King", "2003", "Fantasy", "#795548"),
};

Console.WriteLine();
Console.WriteLine($"â”â”â” MP4 movie-series fixtures (scenarios 33-37) â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”");
if (ffmpegPath is null)
{
    Console.WriteLine("  âœ—  FFmpeg not found â€” cannot create MP4 files.");
    failed += movieSeries.Length;
}
else
{
    foreach (var spec in movieSeries)
    {
        var tempMovieDir = Path.Combine(tempDir, spec.Subdir);
        var finalMovieDir = Path.Combine(moviesDir, spec.Subdir);
        Directory.CreateDirectory(tempMovieDir);
        var outPath = Path.Combine(tempMovieDir, spec.FileName);
        var finalPath = Path.Combine(finalMovieDir, spec.FileName);
        try
        {
            CreateMp4(ffmpegPath, outPath, spec);
            generatedFiles.Add((outPath, finalPath));
            var displayPath = Path.Combine(spec.Subdir, spec.FileName).Replace('\\', '/');
            Console.WriteLine($"  âœ“  [{spec.Scenario,2}] {displayPath}");
            manifest.Add(new(spec.Scenario, displayPath, "mp4", $"Scenario {spec.Scenario} â€” movie series"));
            total++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  âœ—  [{spec.Scenario,2}] {spec.FileName}: {ex.Message}");
            failed++;
        }
    }
}
// â”€â”€ Batch copy to output â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
var tvSeries = new VideoSpec[]
{
    new(38, Path.Combine("breaking-bad", "Season 01"), "Breaking Bad S01E01 Pilot (2008) {imdb-tt0959621}.mp4", "Breaking Bad: Pilot", "2008", "Drama", "#2E7D32"),
    new(39, Path.Combine("breaking-bad", "Season 01"), "Breaking Bad S01E02 Cat's in the Bag (2008) {imdb-tt1054724}.mp4", "Breaking Bad: Cat's in the Bag", "2008", "Drama", "#33691E"),
};

var musicTracks = new MusicSpec[]
{
    new(40, Path.Combine("David Bowie", "The Rise and Fall of Ziggy Stardust"), "01 Five Years.mp3", "Five Years", "David Bowie", "The Rise and Fall of Ziggy Stardust and the Spiders from Mars", "1972", "Rock", "1"),
    new(41, Path.Combine("David Bowie", "The Rise and Fall of Ziggy Stardust"), "02 Soul Love.mp3", "Soul Love", "David Bowie", "The Rise and Fall of Ziggy Stardust and the Spiders from Mars", "1972", "Rock", "2"),
};

var comics = new ComicSpec[]
{
    new(42, "watchmen", "Watchmen 001 (1986).cbz", "Watchmen", "1", "Alan Moore", "Dave Gibbons", "1986"),
    new(43, "watchmen", "Watchmen 002 (1986).cbz", "Watchmen", "2", "Alan Moore", "Dave Gibbons", "1986"),
};

Console.WriteLine();
Console.WriteLine("TV fixtures (scenarios 38-39)");
if (ffmpegPath is null)
{
    Console.WriteLine("  FFmpeg not found - cannot create TV MP4 files.");
    failed += tvSeries.Length;
}
else
{
    foreach (var spec in tvSeries)
    {
        var tempTvDir = Path.Combine(tempDir, "tv", spec.Subdir);
        var finalTvDir = Path.Combine(tvDir, spec.Subdir);
        Directory.CreateDirectory(tempTvDir);
        var outPath = Path.Combine(tempTvDir, spec.FileName);
        var finalPath = Path.Combine(finalTvDir, spec.FileName);
        try
        {
            CreateMp4(ffmpegPath, outPath, spec);
            generatedFiles.Add((outPath, finalPath));
            var displayPath = Path.Combine("tv", spec.Subdir, spec.FileName).Replace('\\', '/');
            Console.WriteLine($"  [{spec.Scenario,2}] {displayPath}");
            manifest.Add(new(spec.Scenario, displayPath, "mp4", $"Scenario {spec.Scenario} - TV episode"));
            total++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [{spec.Scenario,2}] {spec.FileName}: {ex.Message}");
            failed++;
        }
    }
}

Console.WriteLine();
Console.WriteLine("Music fixtures (scenarios 40-41)");
if (ffmpegPath is null)
{
    Console.WriteLine("  FFmpeg not found - cannot create MP3 files.");
    failed += musicTracks.Length;
}
else
{
    foreach (var spec in musicTracks)
    {
        var tempMusicDir = Path.Combine(tempDir, "music", spec.Subdir);
        var finalMusicDir = Path.Combine(musicDir, spec.Subdir);
        Directory.CreateDirectory(tempMusicDir);
        var outPath = Path.Combine(tempMusicDir, spec.FileName);
        var finalPath = Path.Combine(finalMusicDir, spec.FileName);
        try
        {
            CreateMp3(ffmpegPath, outPath, spec);
            generatedFiles.Add((outPath, finalPath));
            var displayPath = Path.Combine("music", spec.Subdir, spec.FileName).Replace('\\', '/');
            Console.WriteLine($"  [{spec.Scenario,2}] {displayPath}");
            manifest.Add(new(spec.Scenario, displayPath, "mp3", $"Scenario {spec.Scenario} - music track"));
            total++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [{spec.Scenario,2}] {spec.FileName}: {ex.Message}");
            failed++;
        }
    }
}

Console.WriteLine();
Console.WriteLine("Comic fixtures (scenarios 42-43)");
foreach (var spec in comics)
{
    var tempComicDir = Path.Combine(tempDir, "comics", spec.Subdir);
    var finalComicDir = Path.Combine(comicsDir, spec.Subdir);
    Directory.CreateDirectory(tempComicDir);
    var outPath = Path.Combine(tempComicDir, spec.FileName);
    var finalPath = Path.Combine(finalComicDir, spec.FileName);
    try
    {
        CreateCbz(outPath, spec);
        generatedFiles.Add((outPath, finalPath));
        var displayPath = Path.Combine("comics", spec.Subdir, spec.FileName).Replace('\\', '/');
        Console.WriteLine($"  [{spec.Scenario,2}] {displayPath}");
        manifest.Add(new(spec.Scenario, displayPath, "cbz", $"Scenario {spec.Scenario} - comic issue"));
        total++;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  [{spec.Scenario,2}] {spec.FileName}: {ex.Message}");
        failed++;
    }
}

Console.WriteLine();
Console.WriteLine("General drop fixture (scenario 44)");
{
    const int num = 44;
    var outPath = Path.Combine(tempDir, "unsorted-field-note.txt");
    var finalPath = Path.Combine(generalDir, "unsorted-field-note.txt");
    try
    {
        File.WriteAllText(outPath, "Tuvima Library general drop-zone smoke fixture.");
        generatedFiles.Add((outPath, finalPath));
        Console.WriteLine($"  [{num,2}] unsorted-field-note.txt");
        manifest.Add(new(num, "unsorted-field-note.txt", "txt", "Scenario 44 - general drop zone"));
        total++;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  [{num,2}] unsorted-field-note.txt: {ex.Message}");
        failed++;
    }
}

Console.WriteLine();
Console.WriteLine($"â”â”â” Copying {generatedFiles.Count} files to watch folder â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”");
foreach (var (src, dst) in generatedFiles)
{
    var dir = Path.GetDirectoryName(dst);
    if (dir is not null) Directory.CreateDirectory(dir);
    File.Copy(src, dst, overwrite: true);
}
Console.WriteLine($"  âœ“  {generatedFiles.Count} files copied to {watchRoot}");

// â”€â”€ Clean up temp â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
try { Directory.Delete(tempDir, recursive: true); } catch { }

// â”€â”€ Write MANIFEST.json â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
// Write MANIFEST.json one level above the watch directory so it is not
// picked up as a media file by the Engine's watch folder monitor.
var manifestPath = Path.Combine(watchRoot, "MANIFEST.json");
var manifestJson = JsonSerializer.Serialize(new
{
    generated_at = DateTimeOffset.UtcNow.ToString("O"),
    watch_root = watchRoot,
    books_directory = booksDir,
    movies_directory = moviesDir,
    tv_directory = tvDir,
    music_directory = musicDir,
    comics_directory = comicsDir,
    total_files = total,
    files = manifest.Select(m => new { scenario = m.Scenario, path = m.Path, type = m.Type, note = m.Note }),
    expected_person_enrichment = expectedPeople.Select(p => new
    {
        name = p.Name,
        expected_wikidata_qid = p.ExpectedWikidataQid,
        minimum_owned_credits = p.MinimumOwnedCredits,
        minimum_media_items = p.MinimumMediaItems,
        expected_media_types = p.ExpectedMediaTypes,
        expected_titles = p.ExpectedTitles,
        require_biography = p.RequireBiography,
        require_headshot = p.RequireHeadshot,
        note = p.Note
    })
}, new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText(manifestPath, manifestJson);

// â”€â”€ Summary â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
Console.WriteLine();
Console.WriteLine($"â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”");
Console.WriteLine($"  Generated : {total} / 44");
if (failed > 0) Console.WriteLine($"  Failed    : {failed}");
Console.WriteLine($"  Manifest  : {manifestPath}");
Console.WriteLine();
Console.WriteLine("Next steps:");
Console.WriteLine("  1. Ensure the Engine is running  (dotnet run --project src/MediaEngine.Api)");
Console.WriteLine("  2. The Engine watches the output directory automatically.");
Console.WriteLine("  3. Check results at http://localhost:61495/swagger or the Dashboard.");
Console.WriteLine();
Console.WriteLine($"â”â”â” Test Coverage Summary â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”");
Console.WriteLine($"  Confidence gates     : 4 scenarios (1, 2, 8, 15)");
Console.WriteLine($"  Series & position    : 4 scenarios (4, 5, 17, 18)");
Console.WriteLine($"  Pseudonyms           : 5 scenarios (5, 6, 7, 16, 21)");
Console.WriteLine($"  Cross-format Collection?     : 4 scenarios (1+11, 5+19)");
Console.WriteLine($"  Narrators            : 2 scenarios (12, 13)");
Console.WriteLine($"  Ingestion hinting    : 4 scenarios (17-18, 19-20)");
Console.WriteLine($"  Corrupt & duplicate  : 2 scenarios (9, 10)");
Console.WriteLine($"  Title disambiguation : 4 scenarios (3, 22, 23, 28)");
Console.WriteLine($"  Foreign language     : 3 scenarios (24, 25, 26)");
Console.WriteLine($"  Multi-author         : 2 scenarios (29, 30)");
Console.WriteLine($"  Same-author diff-work: 1 scenario  (27)");
Console.WriteLine($"  All media watch roots: 7 scenarios (38-44)");
Console.WriteLine($"  Repeated-person checks: {expectedPeople.Length} people declared in MANIFEST.json");
Console.WriteLine($"  Total: 44 files covering 14 test categories");
Console.WriteLine();

return failed > 0 ? 1 : 0;

// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
// Helpers
// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

static string? FindFfmpeg()
{
    var dir = AppContext.BaseDirectory;
    for (int i = 0; i < 8; i++)
    {
        var candidate = Path.Combine(dir, "tools", "ffmpeg", "ffmpeg.exe");
        if (File.Exists(candidate)) return candidate;
        var parent = Directory.GetParent(dir);
        if (parent is null) break;
        dir = parent.FullName;
    }
    foreach (var p in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
    {
        var candidate = Path.Combine(p.Trim(), "ffmpeg.exe");
        if (File.Exists(candidate)) return candidate;
    }
    return null;
}

static byte[]? GeneratePng(string ffmpegPath, string tempDir, string hex, int width, int height)
{
    var outFile = Path.Combine(tempDir, $"cover_{Guid.NewGuid():N}.png");
    var args = $"-y -f lavfi -i \"color=c={hex}:s={width}x{height}:r=1\" -vframes 1 \"{outFile}\"";
    RunFfmpeg(ffmpegPath, args);
    if (!File.Exists(outFile)) return null;
    var bytes = File.ReadAllBytes(outFile);
    File.Delete(outFile);
    return bytes;
}

static void CreateM4b(string ffmpegPath, string tempDir, string outPath, M4bSpec spec, byte[]? cover)
{
    var uid        = Guid.NewGuid().ToString("N");
    var silentFile = Path.Combine(tempDir, $"silent_{uid}.m4a");
    var coverFile  = cover is not null ? Path.Combine(tempDir, $"cover_{uid}.png") : null;

    RunFfmpeg(ffmpegPath,
        $"-y -f lavfi -i \"anullsrc=r=44100:cl=mono\" -t 30 " +
        $"-c:a aac -b:a 32k \"{silentFile}\"");

    if (cover is not null && coverFile is not null)
        File.WriteAllBytes(coverFile, cover);

    var metaArgs = new StringBuilder();
    if (!string.IsNullOrWhiteSpace(spec.Title))       metaArgs.Append($" -metadata title={Q(spec.Title)}");
    if (!string.IsNullOrWhiteSpace(spec.Artist))      metaArgs.Append($" -metadata artist={Q(spec.Artist)}");
    if (!string.IsNullOrWhiteSpace(spec.AlbumArtist)) metaArgs.Append($" -metadata album_artist={Q(spec.AlbumArtist)}");
    if (!string.IsNullOrWhiteSpace(spec.Album))       metaArgs.Append($" -metadata album={Q(spec.Album)}");
    if (!string.IsNullOrWhiteSpace(spec.Narrator))
    {
        // Write narrator to the Composers tag (TagLib primary extraction path)
        // and to Comment with "Narrated by" prefix (secondary extraction path).
        metaArgs.Append($" -metadata composer={Q(spec.Narrator)}");
        metaArgs.Append($" -metadata comment={Q("Narrated by " + spec.Narrator)}");
    }
    if (!string.IsNullOrWhiteSpace(spec.Year))        metaArgs.Append($" -metadata date={Q(spec.Year)}");
    if (!string.IsNullOrWhiteSpace(spec.Genre))       metaArgs.Append($" -metadata genre={Q(spec.Genre)}");
    if (!string.IsNullOrWhiteSpace(spec.TrackNum))    metaArgs.Append($" -metadata track={Q(spec.TrackNum)}");

    string inputArgs, mapArgs, dispArgs;
    if (coverFile is not null)
    {
        inputArgs = $"-i \"{silentFile}\" -i \"{coverFile}\"";
        mapArgs   = "-map 0:a -map 1:v";
        dispArgs  = "-c:a copy -c:v png -disposition:v:0 attached_pic";
    }
    else
    {
        inputArgs = $"-i \"{silentFile}\"";
        mapArgs   = "-map 0:a";
        dispArgs  = "-c:a copy";
    }

    RunFfmpeg(ffmpegPath,
        $"-y {inputArgs} {mapArgs} {dispArgs}{metaArgs} " +
        $"-movflags +faststart \"{outPath}\"");

    TryDelete(silentFile);
    if (coverFile is not null) TryDelete(coverFile);
}

static void RunFfmpeg(string ffmpegPath, string args)
{
    var psi = new ProcessStartInfo(ffmpegPath, args)
    {
        RedirectStandardOutput = true,
        RedirectStandardError  = true,
        UseShellExecute        = false,
        CreateNoWindow         = true,
    };
    using var p = Process.Start(psi) ?? throw new Exception("Failed to start FFmpeg");
    var stdoutTask = p.StandardOutput.ReadToEndAsync();
    var stderrTask = p.StandardError.ReadToEndAsync();
    bool exited = p.WaitForExit(120_000);
    Task.WaitAll(stdoutTask, stderrTask);
    if (!exited) throw new Exception("FFmpeg timed out after 120 seconds");
    if (p.ExitCode != 0)
    {
        var err = stderrTask.Result;
        throw new Exception($"FFmpeg exited {p.ExitCode}: {err[..Math.Min(400, err.Length)]}");
    }
}

static void TryDelete(string path) { try { File.Delete(path); } catch { } }

static string Q(string value) => $"\"{value.Replace("\"", "\\\"")}\"";

static void CreateMp4(string ffmpegPath, string outPath, VideoSpec spec)
{
    var args =
        $"-y -f lavfi -i \"color=c={spec.ColorHex}:s=1280x720:r=24\" -f lavfi -i \"anullsrc=r=48000:cl=stereo\" -t 8 " +
        "-c:v libx264 -pix_fmt yuv420p -c:a aac -b:a 96k " +
        $"-metadata title={Q(spec.Title)} " +
        $"-metadata date={Q(spec.Year)} " +
        $"-metadata genre={Q(spec.Genre)} " +
        $"\"{outPath}\"";
    RunFfmpeg(ffmpegPath, args);
}

static void CreateMp3(string ffmpegPath, string outPath, MusicSpec spec)
{
    var args =
        $"-y -f lavfi -i \"anullsrc=r=44100:cl=stereo\" -t 12 " +
        "-c:a libmp3lame -b:a 128k " +
        $"-metadata title={Q(spec.Title)} " +
        $"-metadata artist={Q(spec.Artist)} " +
        $"-metadata album={Q(spec.Album)} " +
        $"-metadata date={Q(spec.Year)} " +
        $"-metadata genre={Q(spec.Genre)} " +
        $"-metadata track={Q(spec.TrackNum)} " +
        $"\"{outPath}\"";
    RunFfmpeg(ffmpegPath, args);
}

static void CreateCbz(string outPath, ComicSpec spec)
{
    if (File.Exists(outPath)) File.Delete(outPath);

    using var fs = new FileStream(outPath, FileMode.Create);
    using var zip = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false);
    AddText(zip, "ComicInfo.xml", $"""
        <?xml version="1.0" encoding="utf-8"?>
        <ComicInfo>
          <Series>{Esc(spec.Series)}</Series>
          <Number>{Esc(spec.IssueNumber)}</Number>
          <Title>{Esc(spec.Series)} #{Esc(spec.IssueNumber)}</Title>
          <Writer>{Esc(spec.Writer)}</Writer>
          <Penciller>{Esc(spec.Artist)}</Penciller>
          <Year>{Esc(spec.Year)}</Year>
          <Publisher>DC Comics</Publisher>
        </ComicInfo>
        """);

    var pageEntry = zip.CreateEntry("page-001.png", CompressionLevel.Optimal);
    using var pageStream = pageEntry.Open();
    pageStream.Write(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADUlEQVR4nGNkYGBgAAAABAABJzQnCgAAAABJRU5ErkJggg=="));
}

static void CreateEpub(string outputPath, EpubSpec spec, byte[]? coverBytes)
{
    if (File.Exists(outputPath)) File.Delete(outputPath);

    using var fs  = new FileStream(outputPath, FileMode.Create);
    using var zip = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false);

    var mime = zip.CreateEntry("mimetype", CompressionLevel.NoCompression);
    using (var w = new StreamWriter(mime.Open(), Encoding.ASCII)) w.Write("application/epub+zip");

    AddText(zip, "META-INF/container.xml", """
        <?xml version="1.0" encoding="UTF-8"?>
        <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
          <rootfiles>
            <rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/>
          </rootfiles>
        </container>
        """);

    bool hasCover = coverBytes is { Length: > 0 };
    if (hasCover)
    {
        var coverEntry = zip.CreateEntry("OEBPS/cover.png", CompressionLevel.Optimal);
        using var cs = coverEntry.Open();
        cs.Write(coverBytes!);
    }

    var identifierXml = !string.IsNullOrWhiteSpace(spec.Isbn)
        ? $"""<dc:identifier id="isbn">{Esc(spec.Isbn)}</dc:identifier>"""
        : $"""<dc:identifier id="uid">urn:uuid:{Guid.NewGuid()}</dc:identifier>""";

    var authorXml = !string.IsNullOrWhiteSpace(spec.Author)
        ? $"""<dc:creator opf:role="aut">{Esc(spec.Author)}</dc:creator>"""
        : "";
    if (spec.SecondAuthor is not null)
        authorXml += $"\n        <dc:creator opf:role=\"aut\">{Esc(spec.SecondAuthor)}</dc:creator>";

    var titleXml     = !string.IsNullOrWhiteSpace(spec.Title)
        ? $"<dc:title>{Esc(spec.Title)}</dc:title>"
        : "<dc:title>Unknown</dc:title>";
    var dateXml      = !string.IsNullOrWhiteSpace(spec.Year)       ? $"<dc:date>{Esc(spec.Year)}</dc:date>"              : "";
    var publisherXml = !string.IsNullOrWhiteSpace(spec.Publisher)   ? $"<dc:publisher>{Esc(spec.Publisher)}</dc:publisher>" : "";
    var descXml      = !string.IsNullOrWhiteSpace(spec.Description) ? $"<dc:description>{Esc(spec.Description)}</dc:description>" : "";
    var seriesXml    = spec.Series is not null
        ? $"""
          <meta property="belongs-to-collection" id="series">{Esc(spec.Series)}</meta>
          <meta refines="#series" property="collection-type">series</meta>
          """ + (spec.SeriesPosition is not null
              ? $"\n        <meta refines=\"#series\" property=\"group-position\">{Esc(spec.SeriesPosition)}</meta>"
              : "")
        : "";
    var coverManifest = hasCover
        ? """<item id="cover-img" href="cover.png" media-type="image/png" properties="cover-image"/>"""
        : "";

    AddText(zip, "OEBPS/content.opf", $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <package xmlns="http://www.idpf.org/2007/opf"
                 xmlns:dc="http://purl.org/dc/elements/1.1/"
                 xmlns:opf="http://www.idpf.org/2007/opf"
                 unique-identifier="uid" version="3.0">
          <metadata>
            {titleXml}
            {authorXml}
            {identifierXml}
            <dc:language>{spec.Language}</dc:language>
            {publisherXml}
            {descXml}
            {dateXml}
            {seriesXml}
            <meta property="dcterms:modified">2025-01-01T00:00:00Z</meta>
          </metadata>
          <manifest>
            <item id="chapter1" href="chapter1.xhtml" media-type="application/xhtml+xml"/>
            <item id="nav" href="nav.xhtml" media-type="application/xhtml+xml" properties="nav"/>
            <item id="toc" href="toc.ncx" media-type="application/x-dtbncx+xml"/>
            {coverManifest}
          </manifest>
          <spine toc="toc">
            <itemref idref="chapter1"/>
          </spine>
        </package>
        """);

    AddText(zip, "OEBPS/toc.ncx", $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <ncx xmlns="http://www.daisy.org/z3986/2005/ncx/" version="2005-1">
          <head><meta name="dtb:uid" content="{Esc(spec.Isbn ?? spec.Title)}"/></head>
          <docTitle><text>{Esc(spec.Title)}</text></docTitle>
          <navMap>
            <navPoint id="np1" playOrder="1">
              <navLabel><text>Chapter 1</text></navLabel>
              <content src="chapter1.xhtml"/>
            </navPoint>
          </navMap>
        </ncx>
        """);

    AddText(zip, "OEBPS/nav.xhtml", $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops">
          <head><title>Navigation</title></head>
          <body>
            <nav epub:type="toc" id="toc">
              <h1>Table of Contents</h1>
              <ol><li><a href="chapter1.xhtml">Chapter 1</a></li></ol>
            </nav>
          </body>
        </html>
        """);

    var authorLine = spec.SecondAuthor is not null
        ? $"{Esc(spec.Author)} &amp; {Esc(spec.SecondAuthor)}"
        : Esc(spec.Author ?? "Unknown");
    AddText(zip, "OEBPS/chapter1.xhtml", $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <html xmlns="http://www.w3.org/1999/xhtml">
          <head><title>{Esc(spec.Title)}</title></head>
          <body>
            <h1>{Esc(spec.Title)}</h1>
            <p>Test EPUB for Tuvima Library pipeline validation.</p>
            <p>Author: {authorLine} â€” Year: {Esc(spec.Year ?? "Unknown")}</p>
          </body>
        </html>
        """);
}

static void AddText(ZipArchive zip, string entryName, string content)
{
    var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
    using var w = new StreamWriter(entry.Open(), Encoding.UTF8);
    w.Write(content.TrimStart());
}

static string Esc(string? s) =>
    (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
             .Replace("\"", "&quot;").Replace("'", "&apos;");

// â”€â”€ Record types â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

record EpubSpec(
    string FileName,
    string Title,
    string? Author,
    string? SecondAuthor,
    string? Isbn,
    string? Year,
    string? Publisher,
    string? Description,
    string? Series,
    string? SeriesPosition,
    string Language,
    bool IncludeCover,
    string CoverHex);

record M4bSpec(
    string FileName,
    string Title,
    string Artist,
    string AlbumArtist,
    string Album,
    string Narrator,
    string Year,
    string Genre,
    string Comment,
    string TrackNum,
    string? Series,
    string? SeriesPos,
    bool IncludeCover,
    string CoverHex);

record VideoSpec(
    int Scenario,
    string Subdir,
    string FileName,
    string Title,
    string Year,
    string Genre,
    string ColorHex);

record MusicSpec(
    int Scenario,
    string Subdir,
    string FileName,
    string Title,
    string Artist,
    string Album,
    string Year,
    string Genre,
    string TrackNum);

record ComicSpec(
    int Scenario,
    string Subdir,
    string FileName,
    string Series,
    string IssueNumber,
    string Writer,
    string Artist,
    string Year);

record ManifestEntry(int Scenario, string Path, string Type, string Note);

record ExpectedPersonEntry(
    string Name,
    string? ExpectedWikidataQid,
    int MinimumOwnedCredits,
    int MinimumMediaItems,
    string[] ExpectedMediaTypes,
    string[] ExpectedTitles,
    bool RequireBiography,
    bool RequireHeadshot,
    string Note);
