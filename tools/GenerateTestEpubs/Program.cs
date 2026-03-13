// ──────────────────────────────────────────────────────────────────────────────
// GenerateTestEpubs — Creates EPUBs + M4B audiobooks for pipeline testing.
//
// Metadata scenarios exercised:
//   • Fully tagged with embedded cover (high confidence, should auto-organize)
//   • Fully tagged, no cover (metadata confidence still high)
//   • Author name conflict (different formats of the same author)
//   • Series metadata for Hub grouping
//   • Multi-author (collaboration)
//   • Filename-only (no OPF/ID3 metadata — low confidence, goes to review)
//   • Pseudonym authors (Richard Bachman→King, Robert Galbraith→Rowling, Iain Banks↔Iain M. Banks)
//   • Same title as both EPUB + M4B (tests Hub cross-format linking)
//   • ISBN bridge correction (wrong author corrected via Wikidata ISBN match)
//   • Standalone detection (single work without franchise/series)
//   • Multi-genre metadata (genre chips rendering)
//   • Books/Audiobooks shared folder (Dune audiobook alongside EPUB)
//
// Usage:
//   dotnet run --project tools/GenerateTestEpubs [output-directory]
//
// Default output: C:\temp\tuvima-watch
// ──────────────────────────────────────────────────────────────────────────────

using System.Diagnostics;
using System.IO.Compression;
using System.Text;

var outputDir = args.Length > 0 ? args[0] : @"C:\temp\tuvima-watch";
var tempDir   = Path.Combine(Path.GetTempPath(), "tuvima-test-gen");
var ffmpegPath = FindFfmpeg();

Directory.CreateDirectory(outputDir);
Directory.CreateDirectory(tempDir);

Console.WriteLine($"Output directory : {outputDir}");
Console.WriteLine($"FFmpeg           : {ffmpegPath ?? "NOT FOUND"}");
Console.WriteLine();

// ── EPUB definitions ────────────────────────────────────────────────────────
//
// Columns: FileName, Title, Author, SecondAuthor, Isbn, Year,
//          Publisher, Description, Series, SeriesPosition,
//          Language, IncludeCover, CoverHex
//
var epubs = new EpubSpec[]
{
    // 1 — Fully tagged, with cover, two authors, real title
    new("good-omens.epub",
        "Good Omens",
        Author: "Terry Pratchett",       SecondAuthor: "Neil Gaiman",
        Isbn: "9780060853983",           Year: "1990",
        Publisher: "Workman Publishing",
        Description: "The Nice and Accurate Prophecies of Agnes Nutter, Witch.",
        Series: null,                    SeriesPosition: null,
        Language: "en",                  IncludeCover: true,
        CoverHex: "#4A90D9"),

    // 2 — Fully tagged, no cover, pioneer cyberpunk
    new("neuromancer.epub",
        "Neuromancer",
        Author: "William Gibson",        SecondAuthor: null,
        Isbn: "9780441569595",           Year: "1984",
        Publisher: "Ace Books",
        Description: "Case was the sharpest data-thief in the matrix.",
        Series: null,                    SeriesPosition: null,
        Language: "en",                  IncludeCover: false,
        CoverHex: "#2C6B3E"),

    // 3 — Fully tagged, with cover, series metadata
    new("the-name-of-the-wind.epub",
        "The Name of the Wind",
        Author: "Patrick Rothfuss",      SecondAuthor: null,
        Isbn: "9780756404079",           Year: "2007",
        Publisher: "DAW Books",
        Description: "I have stolen princesses back from sleeping barrow kings.",
        Series: "The Kingkiller Chronicle",  SeriesPosition: "1",
        Language: "en",                  IncludeCover: true,
        CoverHex: "#8B4513"),

    // 4 — Author name format conflict: "Asimov, Isaac" vs "Isaac Asimov"
    new("foundation.epub",
        "Foundation",
        Author: "Asimov, Isaac",          SecondAuthor: null,   // reversed format
        Isbn: "9780553293357",            Year: "1951",
        Publisher: "Gnome Press",
        Description: "The Foundation series is set in the far future.",
        Series: "Foundation",            SeriesPosition: "1",
        Language: "en",                  IncludeCover: false,
        CoverHex: "#1A237E"),

    // 5 — Fully tagged, with cover, recent award winner
    new("project-hail-mary.epub",
        "Project Hail Mary",
        Author: "Andy Weir",             SecondAuthor: null,
        Isbn: "9780593135204",           Year: "2021",
        Publisher: "Ballantine Books",
        Description: "A lone astronaut must save Earth from disaster.",
        Series: null,                    SeriesPosition: null,
        Language: "en",                  IncludeCover: true,
        CoverHex: "#1565C0"),

    // 6 — Same title as M4B #2 (Dune) — tests Hub cross-format linking
    new("dune.epub",
        "Dune",
        Author: "Frank Herbert",         SecondAuthor: null,
        Isbn: "9780441172719",           Year: "1965",
        Publisher: "Ace Books",
        Description: "A science fiction masterpiece set on the desert planet Arrakis.",
        Series: "Dune Chronicles",      SeriesPosition: "1",
        Language: "en",                  IncludeCover: true,
        CoverHex: "#B5651D"),

    // 7 — Fully tagged with cover, popular fantasy
    new("the-hobbit.epub",
        "The Hobbit",
        Author: "J.R.R. Tolkien",        SecondAuthor: null,
        Isbn: "9780547928227",           Year: "1937",
        Publisher: "Houghton Mifflin Harcourt",
        Description: "Bilbo Baggins is whisked away on an unexpected adventure.",
        Series: null,                    SeriesPosition: null,
        Language: "en",                  IncludeCover: true,
        CoverHex: "#4E342E"),

    // 8 — PSEUDONYM: Richard Bachman is Stephen King's pen name (Q3324300 → Q39829)
    new("the-running-man.epub",
        "The Running Man",
        Author: "Richard Bachman",       SecondAuthor: null,
        Isbn: "9780451197962",           Year: "1982",
        Publisher: "Signet",
        Description: "A desperate man enters a deadly game show in a dystopian future.",
        Series: null,                    SeriesPosition: null,
        Language: "en",                  IncludeCover: true,
        CoverHex: "#8B0000"),

    // 9 — PSEUDONYM: Robert Galbraith is J.K. Rowling's pen name (Q16308388 → Q34660)
    new("the-cuckoos-calling.epub",
        "The Cuckoo's Calling",
        Author: "Robert Galbraith",      SecondAuthor: null,
        Isbn: "9780316206846",           Year: "2013",
        Publisher: "Mulholland Books",
        Description: "A brilliant mystery novel featuring private detective Cormoran Strike.",
        Series: "Cormoran Strike",       SeriesPosition: "1",
        Language: "en",                  IncludeCover: true,
        CoverHex: "#2F4F4F"),

    // 10 — FICTIONAL — filename-only (all OPF metadata blank/empty)
    new("phantom-signal-filename-only.epub",
        Title: "",                        Author: "",
        SecondAuthor: null,
        Isbn: "",                         Year: "",
        Publisher: "",                    Description: "",
        Series: null,                    SeriesPosition: null,
        Language: "en",                  IncludeCover: false,
        CoverHex: "#212121"),

    // 11 — ISBN bridge correction: correct ISBN but author misspelled
    //       ISBN 9780441172719 maps to Dune (Q190159) by Frank Herbert — "Frank Herber" should be overridden
    new("isbn-wrong-author.epub",
        "Dune",
        Author: "Frank Herber",          SecondAuthor: null,     // TYPO — should be corrected via ISBN bridge
        Isbn: "9780441172719",           Year: "1965",
        Publisher: "Ace Books",
        Description: "A science fiction masterpiece set on the desert planet Arrakis.",
        Series: null,                    SeriesPosition: null,
        Language: "en",                  IncludeCover: false,
        CoverHex: "#B5651D"),

    // 12 — Standalone detection: The Martian has no franchise/series in Wikidata
    //       Should get narrative_scope = "standalone", NOT create a narrative root
    new("standalone-martian.epub",
        "The Martian",
        Author: "Andy Weir",             SecondAuthor: null,
        Isbn: "9780553418026",           Year: "2014",
        Publisher: "Broadway Books",
        Description: "An astronaut is stranded alone on Mars and must survive.",
        Series: null,                    SeriesPosition: null,
        Language: "en",                  IncludeCover: true,
        CoverHex: "#BF360C"),

    // 13 — Multi-genre: tests genre chip rendering with multiple values
    new("multi-genre.epub",
        "The Left Hand of Darkness",
        Author: "Ursula K. Le Guin",     SecondAuthor: null,
        Isbn: "9780441478125",           Year: "1969",
        Publisher: "Ace Books",
        Description: "An envoy explores a world where people have no fixed gender.",
        Series: null,                    SeriesPosition: null,
        Language: "en",                  IncludeCover: true,
        CoverHex: "#1A237E"),

    // 14 — PSEUDONYM (dual-author): James S.A. Corey = Daniel Abraham + Ty Franck
    new("leviathan-wakes.epub",
        "Leviathan Wakes",
        Author: "James S.A. Corey",      SecondAuthor: null,
        Isbn: "9780316129084",           Year: "2011",
        Publisher: "Orbit",
        Description: "Humanity has colonized the solar system.",
        Series: "The Expanse",           SeriesPosition: "1",
        Language: "en",                  IncludeCover: true,
        CoverHex: "#1A237E"),

    // 15 — PSEUDONYM (dual-author): second Expanse book for series test
    new("calibans-war.epub",
        "Caliban's War",
        Author: "James S.A. Corey",      SecondAuthor: null,
        Isbn: "9780316129060",           Year: "2012",
        Publisher: "Orbit",
        Description: "The second book in the Expanse series.",
        Series: "The Expanse",           SeriesPosition: "2",
        Language: "en",                  IncludeCover: true,
        CoverHex: "#0D47A1"),

    // 16 — Multi-author with explicit ordering (primary author first in OPF)
    new("the-talisman.epub",
        "The Talisman",
        Author: "Stephen King",          SecondAuthor: "Peter Straub",
        Isbn: "9781501192272",           Year: "1984",
        Publisher: "Viking Press",
        Description: "A boy sets out on a quest across parallel worlds.",
        Series: null,                    SeriesPosition: null,
        Language: "en",                  IncludeCover: true,
        CoverHex: "#4A148C"),
};

// ── M4B definitions ─────────────────────────────────────────────────────────
//
// Audiobook covers MUST be square (1:1 aspect ratio).
// Columns: FileName, Title, Artist (author), AlbumArtist, Album, Narrator,
//          Year, Genre, Comment, TrackNum, Series, SeriesPos,
//          IncludeCover, CoverHex
//
var m4bs = new M4bSpec[]
{
    // 1 — Fully tagged, square cover, famous UK narrator
    new("harry-potter-philosophers-stone.m4b",
        Title: "Harry Potter and the Philosopher's Stone",
        Artist: "J.K. Rowling",          AlbumArtist: "J.K. Rowling",
        Album: "Harry Potter",           Narrator: "Jim Dale",
        Year: "1997",                    Genre: "Fantasy",
        Comment: "Narrated by Jim Dale",
        TrackNum: "1",
        Series: "Harry Potter",          SeriesPos: "1",
        IncludeCover: true,              CoverHex: "#7B1FA2"),

    // 2 — Same title as EPUB dune.epub — tests Hub cross-format linking
    new("dune-audiobook.m4b",
        Title: "Dune",
        Artist: "Frank Herbert",         AlbumArtist: "Frank Herbert",
        Album: "Dune",                   Narrator: "Scott Brick",
        Year: "1965",                    Genre: "Science Fiction",
        Comment: "Narrated by Scott Brick",
        TrackNum: "1",
        Series: "Dune Chronicles",      SeriesPos: "1",
        IncludeCover: false,             CoverHex: "#B5651D"),

    // 3 — Fully tagged, square cover, comedic sci-fi
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

    // 4 — Series + cover, fantasy epic
    new("mistborn-the-final-empire.m4b",
        Title: "Mistborn: The Final Empire",
        Artist: "Brandon Sanderson",     AlbumArtist: "Brandon Sanderson",
        Album: "Mistborn",               Narrator: "Michael Kramer",
        Year: "2006",                    Genre: "Fantasy",
        Comment: "Narrated by Michael Kramer",
        TrackNum: "1",
        Series: "Mistborn",              SeriesPos: "1",
        IncludeCover: true,              CoverHex: "#37474F"),

    // 5 — No cover art — confidence test
    new("enders-game.m4b",
        Title: "Ender's Game",
        Artist: "Orson Scott Card",      AlbumArtist: "Orson Scott Card",
        Album: "Ender's Game",           Narrator: "Stefan Rudnicki",
        Year: "1985",                    Genre: "Science Fiction",
        Comment: "Narrated by Stefan Rudnicki",
        TrackNum: "1",
        Series: "Ender's Saga",          SeriesPos: "1",
        IncludeCover: false,             CoverHex: "#006064"),

    // 6 — Narrator in comment field, square cover
    new("the-martian.m4b",
        Title: "The Martian",
        Artist: "Andy Weir",             AlbumArtist: "Andy Weir",
        Album: "The Martian",            Narrator: "R.C. Bray",
        Year: "2011",                    Genre: "Science Fiction",
        Comment: "Narrated by R.C. Bray",
        TrackNum: "1",
        Series: null,                    SeriesPos: null,
        IncludeCover: true,              CoverHex: "#BF360C"),

    // 7 — Non-fiction, author IS narrator
    new("a-short-history-of-nearly-everything.m4b",
        Title: "A Short History of Nearly Everything",
        Artist: "Bill Bryson",           AlbumArtist: "Bill Bryson",
        Album: "A Short History of Nearly Everything",
        Narrator: "Bill Bryson",
        Year: "2003",                    Genre: "Non-Fiction",
        Comment: "Read by the author, Bill Bryson",
        TrackNum: "1",
        Series: null,                    SeriesPos: null,
        IncludeCover: true,              CoverHex: "#1B5E20"),

    // 8 — Multiple narrator conflict simulation (tagged with two names in comment)
    new("wool-omnibus.m4b",
        Title: "Wool",
        Artist: "Hugh Howey",            AlbumArtist: "Hugh Howey",
        Album: "Wool Omnibus",           Narrator: "Amanda Donahoe and Tim Gerard Reynolds",
        Year: "2012",                    Genre: "Science Fiction",
        Comment: "Narrated by Amanda Donahoe and Tim Gerard Reynolds",
        TrackNum: "1",
        Series: "Silo",                  SeriesPos: "1",
        IncludeCover: true,              CoverHex: "#4E342E"),

    // 9 — PSEUDONYM: Iain Banks (Q14469) has pseudonym Iain M. Banks (Q214540) via P742
    new("the-wasp-factory.m4b",
        Title: "The Wasp Factory",
        Artist: "Iain Banks",           AlbumArtist: "Iain Banks",
        Album: "The Wasp Factory",      Narrator: "Peter Kenny",
        Year: "1984",                    Genre: "Fiction",
        Comment: "Narrated by Peter Kenny",
        TrackNum: "1",
        Series: null,                    SeriesPos: null,
        IncludeCover: true,              CoverHex: "#4A0E0E"),

    // 10 — FICTIONAL — filename-only (no ID3 tags)
    new("echoes-filename-only.m4b",
        Title: "",                        Artist: "",
        AlbumArtist: "",                 Album: "",
        Narrator: "",                    Year: "",
        Genre: "",                        Comment: "",
        TrackNum: "",
        Series: null,                    SeriesPos: null,
        IncludeCover: false,             CoverHex: "#212121"),

    // 11 — Dune audiobook for shared-folder test: should land in Books/Dune (Q190159)/ alongside dune.epub
    new("dune-audiobook-shared.m4b",
        Title: "Dune",
        Artist: "Frank Herbert",         AlbumArtist: "Frank Herbert",
        Album: "Dune",                   Narrator: "Scott Brick",
        Year: "1965",                    Genre: "Science Fiction",
        Comment: "Narrated by Scott Brick — shared folder test",
        TrackNum: "1",
        Series: "Dune Chronicles",      SeriesPos: "1",
        IncludeCover: true,              CoverHex: "#B5651D"),
};

// ── Generate ─────────────────────────────────────────────────────────────────

int total = 0, failed = 0;

Console.WriteLine($"━━━ Generating {epubs.Length} EPUBs ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
foreach (var spec in epubs)
{
    var outPath = Path.Combine(outputDir, spec.FileName);
    try
    {
        byte[]? cover = null;
        if (spec.IncludeCover && ffmpegPath is not null)
            cover = GeneratePng(ffmpegPath, tempDir, spec.CoverHex, 400, 600);

        CreateEpub(outPath, spec, cover);
        Console.WriteLine($"  ✓  {spec.FileName,-44} {(cover is not null ? "[cover]" : "[no cover]")}");
        total++;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  ✗  {spec.FileName}: {ex.Message}");
        failed++;
    }
}

Console.WriteLine();
Console.WriteLine($"━━━ Generating {m4bs.Length} M4Bs  ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

if (ffmpegPath is null)
{
    Console.WriteLine("  ✗  FFmpeg not found — cannot create M4B files.");
    Console.WriteLine("     Run: powershell -ExecutionPolicy Bypass -File tools/Download-FFmpeg.ps1");
    failed += m4bs.Length;
}
else
{
    foreach (var spec in m4bs)
    {
        var outPath = Path.Combine(outputDir, spec.FileName);
        try
        {
            byte[]? cover = spec.IncludeCover
                ? GeneratePng(ffmpegPath, tempDir, spec.CoverHex, 400, 400)   // SQUARE for audiobooks
                : null;

            CreateM4b(ffmpegPath, tempDir, outPath, spec, cover);
            Console.WriteLine($"  ✓  {spec.FileName,-44} {(cover is not null ? "[square cover]" : "[no cover]  ")}  {(spec.IncludeCover ? "ASIN placeholder" : "filename-only")}");
            total++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✗  {spec.FileName}: {ex.Message}");
            failed++;
        }
    }
}

// ── Clean up temp ─────────────────────────────────────────────────────────────
try { Directory.Delete(tempDir, recursive: true); } catch { }

Console.WriteLine();
Console.WriteLine($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
Console.WriteLine($"  Generated : {total}");
if (failed > 0)
    Console.WriteLine($"  Failed    : {failed}");
Console.WriteLine($"  Output    : {outputDir}");
Console.WriteLine();
Console.WriteLine("Drop all files into the Engine watch folder and start (or restart) the Engine.");

return failed > 0 ? 1 : 0;

// ── FFmpeg helpers ─────────────────────────────────────────────────────────────

static string? FindFfmpeg()
{
    // Walk up from exe location looking for tools/ffmpeg/ffmpeg.exe
    var dir = AppContext.BaseDirectory;
    for (int i = 0; i < 8; i++)
    {
        var candidate = Path.Combine(dir, "tools", "ffmpeg", "ffmpeg.exe");
        if (File.Exists(candidate)) return candidate;
        var parent = Directory.GetParent(dir);
        if (parent is null) break;
        dir = parent.FullName;
    }
    // Fall back to PATH
    foreach (var p in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
    {
        var candidate = Path.Combine(p.Trim(), "ffmpeg.exe");
        if (File.Exists(candidate)) return candidate;
    }
    return null;
}

// Generate a solid-color PNG at the given dimensions using FFmpeg lavfi.
// Returns raw PNG bytes, or null on failure.
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

// Create a silent M4B with optional cover art and ID3-style metadata.
static void CreateM4b(string ffmpegPath, string tempDir, string outPath, M4bSpec spec, byte[]? cover)
{
    var uid = Guid.NewGuid().ToString("N");
    var silentFile  = Path.Combine(tempDir, $"silent_{uid}.m4a");
    var coverFile   = cover is not null ? Path.Combine(tempDir, $"cover_{uid}.png") : null;

    // Step 1: generate 30s of silent AAC audio
    RunFfmpeg(ffmpegPath,
        $"-y -f lavfi -i \"anullsrc=r=44100:cl=mono\" -t 30 " +
        $"-c:a aac -b:a 32k \"{silentFile}\"");

    if (cover is not null && coverFile is not null)
        File.WriteAllBytes(coverFile, cover);

    // Step 2: mux audio + optional cover + metadata into M4B
    var metaArgs = new StringBuilder();
    if (!string.IsNullOrWhiteSpace(spec.Title))
        metaArgs.Append($" -metadata title={Q(spec.Title)}");
    if (!string.IsNullOrWhiteSpace(spec.Artist))
        metaArgs.Append($" -metadata artist={Q(spec.Artist)}");
    if (!string.IsNullOrWhiteSpace(spec.AlbumArtist))
        metaArgs.Append($" -metadata album_artist={Q(spec.AlbumArtist)}");
    if (!string.IsNullOrWhiteSpace(spec.Album))
        metaArgs.Append($" -metadata album={Q(spec.Album)}");
    if (!string.IsNullOrWhiteSpace(spec.Narrator))
        metaArgs.Append($" -metadata comment={Q(spec.Narrator)}");
    if (!string.IsNullOrWhiteSpace(spec.Year))
        metaArgs.Append($" -metadata date={Q(spec.Year)}");
    if (!string.IsNullOrWhiteSpace(spec.Genre))
        metaArgs.Append($" -metadata genre={Q(spec.Genre)}");
    if (!string.IsNullOrWhiteSpace(spec.TrackNum))
        metaArgs.Append($" -metadata track={Q(spec.TrackNum)}");

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

    // Cleanup temp files
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

    // Drain stdout + stderr asynchronously to prevent buffer-full deadlocks.
    // FFmpeg writes verbose output to stderr; if we don't read it the 4 KB
    // pipe buffer fills, FFmpeg blocks, and WaitForExit times out.
    var stdoutTask = p.StandardOutput.ReadToEndAsync();
    var stderrTask = p.StandardError.ReadToEndAsync();

    bool exited = p.WaitForExit(120_000);
    Task.WaitAll(stdoutTask, stderrTask);

    if (!exited)
        throw new Exception("FFmpeg timed out after 120 seconds");

    if (p.ExitCode != 0)
    {
        var err = stderrTask.Result;
        throw new Exception($"FFmpeg exited {p.ExitCode}: {err[..Math.Min(400, err.Length)]}");
    }
}

static void TryDelete(string path) { try { File.Delete(path); } catch { } }

static string Q(string value) => $"\"{value.Replace("\"", "\\\"")}\"";

// ── EPUB creation ──────────────────────────────────────────────────────────────

static void CreateEpub(string outputPath, EpubSpec spec, byte[]? coverBytes)
{
    if (File.Exists(outputPath)) File.Delete(outputPath);

    using var fs  = new FileStream(outputPath, FileMode.Create);
    using var zip = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false);

    // 1. mimetype (must be first, uncompressed)
    var mime = zip.CreateEntry("mimetype", CompressionLevel.NoCompression);
    using (var w = new StreamWriter(mime.Open(), Encoding.ASCII)) w.Write("application/epub+zip");

    // 2. META-INF/container.xml
    AddText(zip, "META-INF/container.xml", """
        <?xml version="1.0" encoding="UTF-8"?>
        <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
          <rootfiles>
            <rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/>
          </rootfiles>
        </container>
        """);

    // 3. OEBPS/cover.png (optional)
    bool hasCover = coverBytes is { Length: > 0 };
    if (hasCover)
    {
        var coverEntry = zip.CreateEntry("OEBPS/cover.png", CompressionLevel.Optimal);
        using var cs = coverEntry.Open();
        cs.Write(coverBytes!);
    }

    // 4. OEBPS/content.opf
    var identifierXml = !string.IsNullOrWhiteSpace(spec.Isbn)
        ? $"""<dc:identifier id="isbn">{Esc(spec.Isbn)}</dc:identifier>"""
        : $"""<dc:identifier id="uid">urn:uuid:{Guid.NewGuid()}</dc:identifier>""";

    var authorXml = !string.IsNullOrWhiteSpace(spec.Author)
        ? $"""<dc:creator opf:role="aut">{Esc(spec.Author)}</dc:creator>"""
        : "";
    if (spec.SecondAuthor is not null)
        authorXml += $"\n        <dc:creator opf:role=\"aut\">{Esc(spec.SecondAuthor)}</dc:creator>";

    var dateXml = !string.IsNullOrWhiteSpace(spec.Year)
        ? $"<dc:date>{Esc(spec.Year)}</dc:date>"
        : "";
    var publisherXml = !string.IsNullOrWhiteSpace(spec.Publisher)
        ? $"<dc:publisher>{Esc(spec.Publisher)}</dc:publisher>"
        : "";
    var descXml = !string.IsNullOrWhiteSpace(spec.Description)
        ? $"<dc:description>{Esc(spec.Description)}</dc:description>"
        : "";
    var seriesXml = spec.Series is not null
        ? $"""
          <meta property="belongs-to-collection" id="series">{Esc(spec.Series)}</meta>
          <meta refines="#series" property="collection-type">series</meta>
          """ + (spec.SeriesPosition is not null
              ? $"\n        <meta refines=\"#series\" property=\"group-position\">{Esc(spec.SeriesPosition)}</meta>"
              : "")
        : "";
    var titleXml = !string.IsNullOrWhiteSpace(spec.Title)
        ? $"<dc:title>{Esc(spec.Title)}</dc:title>"
        : "<dc:title>Unknown</dc:title>";
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

    // 5. OEBPS/toc.ncx
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

    // 6. OEBPS/nav.xhtml
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

    // 7. OEBPS/chapter1.xhtml
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
            <p>Author: {authorLine} — Year: {Esc(spec.Year ?? "Unknown")}</p>
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

// ── Record types ───────────────────────────────────────────────────────────────

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
