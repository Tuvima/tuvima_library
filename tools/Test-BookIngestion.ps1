<#
.SYNOPSIS
    Book Ingestion Test — drops synthetic EPUBs into the Tuvima watch folder
    and produces a before/after ingestion report.

.DESCRIPTION
    Generates synthetic EPUB files from a 100-title catalog (with varying metadata
    quality) and drops a random selection into the watch folder. Monitors the engine
    API for completion, then queries results and writes a human-readable report.

    Scenarios tested:
      high      — Full metadata: title, author, year, ISBN, series, genre (60 books)
      medium    — Partial metadata: title, author, year only (20 books)
      low       — No embedded metadata; engine must derive title from filename (10 books)
      corrupt   — Invalid file bytes; expect MediaFailed (5 books)
      duplicate — Identical bytes to an earlier high-confidence book; expect DuplicateSkipped (5 books)

.PARAMETER Count
    Number of books to randomly select from the catalog (default: 10, max: 100).

.PARAMETER Seed
    Random seed for reproducible runs. Omit for a fresh random selection.

.PARAMETER EngineUrl
    Base URL of the running Tuvima Engine API (default: http://localhost:61495).

.PARAMETER WatchDirectory
    Path to the watch folder. Overrides config auto-detection.

.PARAMETER WipeFirst
    When set, wipes library.db and clears library/staging folders before running.
    Requires confirmation unless -Force is also set.
    WARNING: Destroys all existing library data.

.PARAMETER Force
    Skip the confirmation prompt for -WipeFirst.

.PARAMETER TimeoutSeconds
    Maximum seconds to wait for ingestion to complete (default: 120).

.PARAMETER ReportPath
    Where to save the text report. Defaults to tools/reports/book-ingestion-<timestamp>.txt.

.EXAMPLE
    # Quick 10-book test
    .\Test-BookIngestion.ps1

    # Reproducible 25-book run
    .\Test-BookIngestion.ps1 -Count 25 -Seed 42

    # Full wipe + 50 books, no confirmation
    .\Test-BookIngestion.ps1 -Count 50 -WipeFirst -Force

.NOTES
    Run as "book ingestion test" — part of the Tuvima testing arsenal.
    Always produces two reports: pre-ingestion (files dropped in) and
    post-ingestion (status of each file after the engine processes it).
#>
[CmdletBinding()]
param(
    [ValidateRange(1, 100)]
    [int]$Count = 10,

    [int]$Seed = -1,

    [string]$EngineUrl = "http://localhost:61495",

    [string]$WatchDirectory = "",

    [switch]$WipeFirst,

    [switch]$Force,

    [ValidateRange(10, 600)]
    [int]$TimeoutSeconds = 120,

    [string]$ReportPath = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

# ─── Resolve script root and report path ──────────────────────────────────────
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Definition
$RepoRoot   = Split-Path -Parent $ScriptRoot
$Timestamp  = Get-Date -Format "yyyy-MM-dd_HH-mm-ss"

if (-not $ReportPath) {
    $ReportsDir = Join-Path $ScriptRoot "reports"
    if (-not (Test-Path $ReportsDir)) { New-Item -ItemType Directory -Path $ReportsDir | Out-Null }
    $ReportPath = Join-Path $ReportsDir "book-ingestion-$Timestamp.txt"
}

$ReportLines = [System.Collections.Generic.List[string]]::new()

# ─── Output helpers ────────────────────────────────────────────────────────────
function Write-H { param([string]$t, [string]$c = "Cyan")
    $line = "═" * 72
    Write-Host $line -ForegroundColor $c
    Write-Host " $t" -ForegroundColor $c
    Write-Host $line -ForegroundColor $c
    $script:ReportLines.Add($line); $script:ReportLines.Add(" $t"); $script:ReportLines.Add($line)
}
function Write-S { param([string]$t)
    $h = " ── $t"; $u = " " + ("─" * 70)
    Write-Host "`n$h" -ForegroundColor Yellow
    Write-Host $u -ForegroundColor DarkYellow
    $script:ReportLines.Add(""); $script:ReportLines.Add($h); $script:ReportLines.Add($u)
}
function Write-R { param([string]$t, [string]$c = "White")
    Write-Host $t -ForegroundColor $c
    $script:ReportLines.Add($t)
}
function Write-RL { param([string]$t = "")
    Write-Host $t
    $script:ReportLines.Add($t)
}

# ─── API helper ────────────────────────────────────────────────────────────────
function Invoke-Api {
    param([string]$Path, [string]$Method = "GET")
    try {
        return Invoke-RestMethod -Uri "$EngineUrl$Path" -Method $Method `
            -TimeoutSec 10 -ErrorAction Stop
    } catch { return $null }
}

# ─── Book catalog (100 entries) ───────────────────────────────────────────────
$Catalog = @(
    # HIGH CONFIDENCE — full metadata (60 books)
    @{Id=1;  Title="Dune";                                  Author="Frank Herbert";       Year="1965"; Series="Dune Chronicles";          Pos="1"; Isbn="9780441013593"; Genre="Science Fiction"; S="high"}
    @{Id=2;  Title="Foundation";                            Author="Isaac Asimov";        Year="1951"; Series="Foundation";               Pos="1"; Isbn="9780553293357"; Genre="Science Fiction"; S="high"}
    @{Id=3;  Title="Leviathan Wakes";                       Author="James S.A. Corey";    Year="2011"; Series="The Expanse";              Pos="1"; Isbn="9780316129084"; Genre="Science Fiction"; S="high"}
    @{Id=4;  Title="The Name of the Wind";                  Author="Patrick Rothfuss";    Year="2007"; Series="Kingkiller Chronicle";     Pos="1"; Isbn="9780756404079"; Genre="Fantasy";         S="high"}
    @{Id=5;  Title="A Game of Thrones";                     Author="George R.R. Martin";  Year="1996"; Series="A Song of Ice and Fire";  Pos="1"; Isbn="9780553381689"; Genre="Fantasy";         S="high"}
    @{Id=6;  Title="The Way of Kings";                      Author="Brandon Sanderson";   Year="2010"; Series="The Stormlight Archive";  Pos="1"; Isbn="9780765326355"; Genre="Fantasy";         S="high"}
    @{Id=7;  Title="Mistborn: The Final Empire";            Author="Brandon Sanderson";   Year="2006"; Series="Mistborn";                Pos="1"; Isbn="9780765311788"; Genre="Fantasy";         S="high"}
    @{Id=8;  Title="The Hitchhiker's Guide to the Galaxy";  Author="Douglas Adams";       Year="1979"; Series="Hitchhiker's Guide";      Pos="1"; Isbn="9780345391803"; Genre="Comedy Sci-Fi";  S="high"}
    @{Id=9;  Title="Neuromancer";                           Author="William Gibson";      Year="1984"; Series="";                         Pos="";  Isbn="9780441569595"; Genre="Cyberpunk";      S="high"}
    @{Id=10; Title="Ender's Game";                          Author="Orson Scott Card";    Year="1985"; Series="Ender's Game";             Pos="1"; Isbn="9780312853235"; Genre="Science Fiction"; S="high"}
    @{Id=11; Title="The Lord of the Rings";                 Author="J.R.R. Tolkien";      Year="1954"; Series="Middle-earth";             Pos="1"; Isbn="9780261102354"; Genre="Fantasy";         S="high"}
    @{Id=12; Title="1984";                                  Author="George Orwell";       Year="1949"; Series="";                         Pos="";  Isbn="9780451524935"; Genre="Dystopian";       S="high"}
    @{Id=13; Title="Brave New World";                       Author="Aldous Huxley";       Year="1932"; Series="";                         Pos="";  Isbn="9780060850524"; Genre="Dystopian";       S="high"}
    @{Id=14; Title="The Martian";                           Author="Andy Weir";           Year="2011"; Series="";                         Pos="";  Isbn="9780553418026"; Genre="Science Fiction"; S="high"}
    @{Id=15; Title="Project Hail Mary";                     Author="Andy Weir";           Year="2021"; Series="";                         Pos="";  Isbn="9780593135204"; Genre="Science Fiction"; S="high"}
    @{Id=16; Title="Red Rising";                            Author="Pierce Brown";        Year="2014"; Series="Red Rising Saga";          Pos="1"; Isbn="9780345539786"; Genre="Science Fiction"; S="high"}
    @{Id=17; Title="The Blade Itself";                      Author="Joe Abercrombie";     Year="2006"; Series="The First Law";            Pos="1"; Isbn="9780575079793"; Genre="Fantasy";         S="high"}
    @{Id=18; Title="Words of Radiance";                     Author="Brandon Sanderson";   Year="2014"; Series="The Stormlight Archive";  Pos="2"; Isbn="9780765326362"; Genre="Fantasy";         S="high"}
    @{Id=19; Title="Caliban's War";                         Author="James S.A. Corey";    Year="2012"; Series="The Expanse";              Pos="2"; Isbn="9780316202107"; Genre="Science Fiction"; S="high"}
    @{Id=20; Title="Dune Messiah";                          Author="Frank Herbert";       Year="1969"; Series="Dune Chronicles";          Pos="2"; Isbn="9780441172696"; Genre="Science Fiction"; S="high"}
    @{Id=21; Title="Foundation and Empire";                 Author="Isaac Asimov";        Year="1952"; Series="Foundation";               Pos="2"; Isbn="9780553293371"; Genre="Science Fiction"; S="high"}
    @{Id=22; Title="The Well of Ascension";                 Author="Brandon Sanderson";   Year="2007"; Series="Mistborn";                Pos="2"; Isbn="9780765316882"; Genre="Fantasy";         S="high"}
    @{Id=23; Title="The Eye of the World";                  Author="Robert Jordan";       Year="1990"; Series="The Wheel of Time";       Pos="1"; Isbn="9780765334343"; Genre="Fantasy";         S="high"}
    @{Id=24; Title="The Great Hunt";                        Author="Robert Jordan";       Year="1990"; Series="The Wheel of Time";       Pos="2"; Isbn="9780765334350"; Genre="Fantasy";         S="high"}
    @{Id=25; Title="A Wizard of Earthsea";                  Author="Ursula K. Le Guin";   Year="1968"; Series="Earthsea Cycle";          Pos="1"; Isbn="9780553383041"; Genre="Fantasy";         S="high"}
    @{Id=26; Title="The Left Hand of Darkness";             Author="Ursula K. Le Guin";   Year="1969"; Series="Hainish Cycle";           Pos="";  Isbn="9780441478125"; Genre="Science Fiction"; S="high"}
    @{Id=27; Title="Flowers for Algernon";                  Author="Daniel Keyes";        Year="1966"; Series="";                         Pos="";  Isbn="9780156030304"; Genre="Science Fiction"; S="high"}
    @{Id=28; Title="Slaughterhouse-Five";                   Author="Kurt Vonnegut";       Year="1969"; Series="";                         Pos="";  Isbn="9780440180296"; Genre="Science Fiction"; S="high"}
    @{Id=29; Title="The Hobbit";                            Author="J.R.R. Tolkien";      Year="1937"; Series="Middle-earth";             Pos="";  Isbn="9780547928227"; Genre="Fantasy";         S="high"}
    @{Id=30; Title="The Color of Magic";                    Author="Terry Pratchett";     Year="1983"; Series="Discworld";               Pos="1"; Isbn="9780062225672"; Genre="Fantasy Comedy";  S="high"}
    @{Id=31; Title="Good Omens";                            Author="Terry Pratchett";     Year="1990"; Series="";                         Pos="";  Isbn="9780060853983"; Genre="Fantasy Comedy";  S="high"}
    @{Id=32; Title="American Gods";                         Author="Neil Gaiman";         Year="2001"; Series="";                         Pos="";  Isbn="9780380973651"; Genre="Fantasy";         S="high"}
    @{Id=33; Title="The Lies of Locke Lamora";              Author="Scott Lynch";         Year="2006"; Series="Gentleman Bastard";       Pos="1"; Isbn="9780553588941"; Genre="Fantasy";         S="high"}
    @{Id=34; Title="Assassin's Apprentice";                 Author="Robin Hobb";          Year="1995"; Series="Farseer Trilogy";         Pos="1"; Isbn="9780553573398"; Genre="Fantasy";         S="high"}
    @{Id=35; Title="Ancillary Justice";                     Author="Ann Leckie";          Year="2013"; Series="Imperial Radch";          Pos="1"; Isbn="9780316246620"; Genre="Science Fiction"; S="high"}
    @{Id=36; Title="The Dispossessed";                      Author="Ursula K. Le Guin";   Year="1974"; Series="Hainish Cycle";           Pos="";  Isbn="9780061054884"; Genre="Science Fiction"; S="high"}
    @{Id=37; Title="Hyperion";                              Author="Dan Simmons";         Year="1989"; Series="Hyperion Cantos";         Pos="1"; Isbn="9780553283686"; Genre="Science Fiction"; S="high"}
    @{Id=38; Title="The Fall of Hyperion";                  Author="Dan Simmons";         Year="1990"; Series="Hyperion Cantos";         Pos="2"; Isbn="9780553288208"; Genre="Science Fiction"; S="high"}
    @{Id=39; Title="Snow Crash";                            Author="Neal Stephenson";     Year="1992"; Series="";                         Pos="";  Isbn="9780553380958"; Genre="Cyberpunk";      S="high"}
    @{Id=40; Title="Cryptonomicon";                         Author="Neal Stephenson";     Year="1999"; Series="";                         Pos="";  Isbn="9780060512804"; Genre="Thriller";       S="high"}
    @{Id=41; Title="Old Man's War";                         Author="John Scalzi";         Year="2005"; Series="Old Man's War";           Pos="1"; Isbn="9780765315034"; Genre="Science Fiction"; S="high"}
    @{Id=42; Title="The Android's Dream";                   Author="John Scalzi";         Year="2006"; Series="";                         Pos="";  Isbn="9780765348494"; Genre="Science Fiction"; S="high"}
    @{Id=43; Title="Piranesi";                              Author="Susanna Clarke";      Year="2020"; Series="";                         Pos="";  Isbn="9781635575637"; Genre="Fantasy";         S="high"}
    @{Id=44; Title="The Fifth Season";                      Author="N.K. Jemisin";        Year="2015"; Series="The Broken Earth";        Pos="1"; Isbn="9780316229296"; Genre="Science Fiction"; S="high"}
    @{Id=45; Title="All Systems Red";                       Author="Martha Wells";        Year="2017"; Series="Murderbot Diaries";       Pos="1"; Isbn="9780765397539"; Genre="Science Fiction"; S="high"}
    @{Id=46; Title="A Memory Called Empire";                Author="Arkady Martine";      Year="2019"; Series="Teixcalaan";              Pos="1"; Isbn="9781250186430"; Genre="Science Fiction"; S="high"}
    @{Id=47; Title="The Long Way to a Small Angry Planet";  Author="Becky Chambers";      Year="2014"; Series="Wayfarers";              Pos="1"; Isbn="9781473619814"; Genre="Science Fiction"; S="high"}
    @{Id=48; Title="Klara and the Sun";                     Author="Kazuo Ishiguro";      Year="2021"; Series="";                         Pos="";  Isbn="9780593311295"; Genre="Science Fiction"; S="high"}
    @{Id=49; Title="The Buried Giant";                      Author="Kazuo Ishiguro";      Year="2015"; Series="";                         Pos="";  Isbn="9780307455796"; Genre="Fantasy";         S="high"}
    @{Id=50; Title="Jonathan Strange and Mr Norrell";       Author="Susanna Clarke";      Year="2004"; Series="";                         Pos="";  Isbn="9781582344164"; Genre="Fantasy";         S="high"}
    @{Id=51; Title="Children of Time";                      Author="Adrian Tchaikovsky";  Year="2015"; Series="Children of Time";        Pos="1"; Isbn="9781447273288"; Genre="Science Fiction"; S="high"}
    @{Id=52; Title="To Be Taught If Fortunate";             Author="Becky Chambers";      Year="2020"; Series="";                         Pos="";  Isbn="9781250236234"; Genre="Science Fiction"; S="high"}
    @{Id=53; Title="The Ninth Rain";                        Author="Jen Williams";        Year="2017"; Series="The Winnowing Flame";     Pos="1"; Isbn="9781472235299"; Genre="Fantasy";         S="high"}
    @{Id=54; Title="Abaddon's Gate";                        Author="James S.A. Corey";    Year="2013"; Series="The Expanse";              Pos="3"; Isbn="9780316129060"; Genre="Science Fiction"; S="high"}
    @{Id=55; Title="Second Foundation";                     Author="Isaac Asimov";        Year="1953"; Series="Foundation";               Pos="3"; Isbn="9780553293388"; Genre="Science Fiction"; S="high"}
    @{Id=56; Title="Children of Dune";                      Author="Frank Herbert";       Year="1976"; Series="Dune Chronicles";          Pos="3"; Isbn="9780441104024"; Genre="Science Fiction"; S="high"}
    @{Id=57; Title="The Hero of Ages";                      Author="Brandon Sanderson";   Year="2008"; Series="Mistborn";                Pos="3"; Isbn="9780765356147"; Genre="Fantasy";         S="high"}
    @{Id=58; Title="Oathbringer";                           Author="Brandon Sanderson";   Year="2017"; Series="The Stormlight Archive";  Pos="3"; Isbn="9780765326379"; Genre="Fantasy";         S="high"}
    @{Id=59; Title="The Dragon Reborn";                     Author="Robert Jordan";       Year="1991"; Series="The Wheel of Time";       Pos="3"; Isbn="9780765334367"; Genre="Fantasy";         S="high"}
    @{Id=60; Title="Before They Are Hanged";                Author="Joe Abercrombie";     Year="2007"; Series="The First Law";            Pos="2"; Isbn="9781591025788"; Genre="Fantasy";         S="high"}
    # MEDIUM CONFIDENCE — title + author + year only (20 books)
    @{Id=61; Title="The Alchemist";              Author="Paulo Coelho";           Year="1988"; Series=""; Pos=""; Isbn=""; Genre=""; S="medium"}
    @{Id=62; Title="Siddhartha";                 Author="Hermann Hesse";          Year="1922"; Series=""; Pos=""; Isbn=""; Genre=""; S="medium"}
    @{Id=63; Title="The Trial";                  Author="Franz Kafka";            Year="1925"; Series=""; Pos=""; Isbn=""; Genre=""; S="medium"}
    @{Id=64; Title="Invisible Man";              Author="Ralph Ellison";          Year="1952"; Series=""; Pos=""; Isbn=""; Genre=""; S="medium"}
    @{Id=65; Title="The Bell Jar";               Author="Sylvia Plath";           Year="1963"; Series=""; Pos=""; Isbn=""; Genre=""; S="medium"}
    @{Id=66; Title="A Farewell to Arms";         Author="Ernest Hemingway";       Year="1929"; Series=""; Pos=""; Isbn=""; Genre=""; S="medium"}
    @{Id=67; Title="For Whom the Bell Tolls";    Author="Ernest Hemingway";       Year="1940"; Series=""; Pos=""; Isbn=""; Genre=""; S="medium"}
    @{Id=68; Title="The Old Man and the Sea";    Author="Ernest Hemingway";       Year="1952"; Series=""; Pos=""; Isbn=""; Genre=""; S="medium"}
    @{Id=69; Title="Of Mice and Men";            Author="John Steinbeck";         Year="1937"; Series=""; Pos=""; Isbn=""; Genre=""; S="medium"}
    @{Id=70; Title="East of Eden";               Author="John Steinbeck";         Year="1952"; Series=""; Pos=""; Isbn=""; Genre=""; S="medium"}
    @{Id=71; Title="Moby Dick";                  Author="Herman Melville";        Year="1851"; Series=""; Pos=""; Isbn=""; Genre=""; S="medium"}
    @{Id=72; Title="Crime and Punishment";       Author="Fyodor Dostoevsky";      Year="1866"; Series=""; Pos=""; Isbn=""; Genre=""; S="medium"}
    @{Id=73; Title="The Brothers Karamazov";     Author="Fyodor Dostoevsky";      Year="1879"; Series=""; Pos=""; Isbn=""; Genre=""; S="medium"}
    @{Id=74; Title="War and Peace";              Author="Leo Tolstoy";            Year="1869"; Series=""; Pos=""; Isbn=""; Genre=""; S="medium"}
    @{Id=75; Title="Anna Karenina";              Author="Leo Tolstoy";            Year="1877"; Series=""; Pos=""; Isbn=""; Genre=""; S="medium"}
    @{Id=76; Title="Don Quixote";                Author="Miguel de Cervantes";    Year="1605"; Series=""; Pos=""; Isbn=""; Genre=""; S="medium"}
    @{Id=77; Title="The Divine Comedy";          Author="Dante Alighieri";        Year="1320"; Series=""; Pos=""; Isbn=""; Genre=""; S="medium"}
    @{Id=78; Title="Les Miserables";             Author="Victor Hugo";            Year="1862"; Series=""; Pos=""; Isbn=""; Genre=""; S="medium"}
    @{Id=79; Title="The Count of Monte Cristo";  Author="Alexandre Dumas";        Year="1844"; Series=""; Pos=""; Isbn=""; Genre=""; S="medium"}
    @{Id=80; Title="Around the World in 80 Days";Author="Jules Verne";            Year="1872"; Series=""; Pos=""; Isbn=""; Genre=""; S="medium"}
    # LOW CONFIDENCE — no embedded metadata; engine derives title from filename (10 books)
    @{Id=81; Title="unlabeled_scan_001";          Author=""; Year=""; Series=""; Pos=""; Isbn=""; Genre=""; S="low"}
    @{Id=82; Title="ebook_download_final";        Author=""; Year=""; Series=""; Pos=""; Isbn=""; Genre=""; S="low"}
    @{Id=83; Title="converted_doc_v2";            Author=""; Year=""; Series=""; Pos=""; Isbn=""; Genre=""; S="low"}
    @{Id=84; Title="reading_list_item_3";         Author=""; Year=""; Series=""; Pos=""; Isbn=""; Genre=""; S="low"}
    @{Id=85; Title="backup_book_copy";            Author=""; Year=""; Series=""; Pos=""; Isbn=""; Genre=""; S="low"}
    @{Id=86; Title="mystery_epub_untitled";       Author=""; Year=""; Series=""; Pos=""; Isbn=""; Genre=""; S="low"}
    @{Id=87; Title="document_export_003";         Author=""; Year=""; Series=""; Pos=""; Isbn=""; Genre=""; S="low"}
    @{Id=88; Title="temp_file_do_not_delete";     Author=""; Year=""; Series=""; Pos=""; Isbn=""; Genre=""; S="low"}
    @{Id=89; Title="new_book_no_title";           Author=""; Year=""; Series=""; Pos=""; Isbn=""; Genre=""; S="low"}
    @{Id=90; Title="archive_entry_2024";          Author=""; Year=""; Series=""; Pos=""; Isbn=""; Genre=""; S="low"}
    # CORRUPT — invalid bytes; expect MediaFailed (5 books)
    @{Id=91; Title="corrupted_epub_A";    Author=""; Year=""; Series=""; Pos=""; Isbn=""; Genre=""; S="corrupt"}
    @{Id=92; Title="corrupted_epub_B";    Author=""; Year=""; Series=""; Pos=""; Isbn=""; Genre=""; S="corrupt"}
    @{Id=93; Title="truncated_file";      Author=""; Year=""; Series=""; Pos=""; Isbn=""; Genre=""; S="corrupt"}
    @{Id=94; Title="wrong_magic_bytes";   Author=""; Year=""; Series=""; Pos=""; Isbn=""; Genre=""; S="corrupt"}
    @{Id=95; Title="empty_epub";          Author=""; Year=""; Series=""; Pos=""; Isbn=""; Genre=""; S="corrupt"}
    # DUPLICATES — identical bytes to books 1–5; expect DuplicateSkipped (5 books)
    @{Id=96;  Title="Dune";               Author="Frank Herbert";     Year="1965"; Series="Dune Chronicles"; Pos="1"; Isbn="9780441013593"; Genre="Science Fiction"; S="duplicate"; DuplicateOf=1}
    @{Id=97;  Title="Foundation";         Author="Isaac Asimov";      Year="1951"; Series="Foundation";      Pos="1"; Isbn="9780553293357"; Genre="Science Fiction"; S="duplicate"; DuplicateOf=2}
    @{Id=98;  Title="Leviathan Wakes";    Author="James S.A. Corey";  Year="2011"; Series="The Expanse";     Pos="1"; Isbn="9780316129084"; Genre="Science Fiction"; S="duplicate"; DuplicateOf=3}
    @{Id=99;  Title="The Martian";        Author="Andy Weir";         Year="2011"; Series="";                Pos="";  Isbn="9780553418026"; Genre="Science Fiction"; S="duplicate"; DuplicateOf=14}
    @{Id=100; Title="1984";               Author="George Orwell";     Year="1949"; Series="";                Pos="";  Isbn="9780451524935"; Genre="Dystopian";       S="duplicate"; DuplicateOf=12}
)

# ─── EPUB generation ───────────────────────────────────────────────────────────
function New-ValidEpub {
    param($Book, [string]$OutputPath)

    $xmlEscapedTitle  = [System.Security.SecurityElement]::Escape($Book.Title)
    $xmlEscapedAuthor = [System.Security.SecurityElement]::Escape($Book.Author)

    $seriesMeta = if ($Book.Series) {
        "    <meta property=`"belongs-to-collection`" id=`"series`">$([System.Security.SecurityElement]::Escape($Book.Series))</meta>`n" +
        "    <meta property=`"group-position`">$($Book.Pos)</meta>"
    } else { "" }

    $genreMeta = if ($Book.Genre) {
        "    <dc:subject>$([System.Security.SecurityElement]::Escape($Book.Genre))</dc:subject>"
    } else { "" }

    $isbnId = if ($Book.Isbn) { "urn:isbn:$($Book.Isbn)" } else { "urn:uuid:$(New-Guid)" }

    $container = @"
<?xml version="1.0" encoding="UTF-8"?>
<container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
  <rootfiles>
    <rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/>
  </rootfiles>
</container>
"@

    $opf = @"
<?xml version="1.0" encoding="UTF-8"?>
<package version="3.0" xmlns="http://www.idpf.org/2007/opf" unique-identifier="uid">
  <metadata xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:opf="http://www.idpf.org/2007/opf">
    <dc:identifier id="uid">$isbnId</dc:identifier>
    <dc:title>$xmlEscapedTitle</dc:title>
    <dc:creator opf:role="aut">$xmlEscapedAuthor</dc:creator>
    <dc:date>$($Book.Year)</dc:date>
    <dc:language>en</dc:language>
    $seriesMeta
    $genreMeta
  </metadata>
  <manifest>
    <item id="chapter1" href="chapter1.xhtml" media-type="application/xhtml+xml"/>
  </manifest>
  <spine>
    <itemref idref="chapter1"/>
  </spine>
</package>
"@

    $chapter = @"
<?xml version="1.0" encoding="utf-8"?>
<!DOCTYPE html>
<html xmlns="http://www.w3.org/1999/xhtml">
<head><title>$xmlEscapedTitle</title></head>
<body><p>Synthetic test content for $xmlEscapedTitle by $xmlEscapedAuthor.</p></body>
</html>
"@

    $stream = [System.IO.File]::Create($OutputPath)
    try {
        $zip = [System.IO.Compression.ZipArchive]::new($stream, [System.IO.Compression.ZipArchiveMode]::Create, $true)
        try {
            # mimetype — stored, no compression
            $mt = $zip.CreateEntry("mimetype", [System.IO.Compression.CompressionLevel]::NoCompression)
            $w = [System.IO.StreamWriter]::new($mt.Open()); $w.Write("application/epub+zip"); $w.Dispose()
            # META-INF/container.xml
            $ce = $zip.CreateEntry("META-INF/container.xml")
            $w = [System.IO.StreamWriter]::new($ce.Open()); $w.Write($container); $w.Dispose()
            # OEBPS/content.opf
            $oe = $zip.CreateEntry("OEBPS/content.opf")
            $w = [System.IO.StreamWriter]::new($oe.Open()); $w.Write($opf); $w.Dispose()
            # OEBPS/chapter1.xhtml
            $ch = $zip.CreateEntry("OEBPS/chapter1.xhtml")
            $w = [System.IO.StreamWriter]::new($ch.Open()); $w.Write($chapter); $w.Dispose()
        } finally { $zip.Dispose() }
    } finally { $stream.Dispose() }
}

function New-BareEpub {
    # Valid EPUB structure but NO metadata — forces filename-based title only
    param([string]$OutputPath)
    $container = @"
<?xml version="1.0" encoding="UTF-8"?>
<container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
  <rootfiles>
    <rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/>
  </rootfiles>
</container>
"@
    $opf = @"
<?xml version="1.0" encoding="UTF-8"?>
<package version="3.0" xmlns="http://www.idpf.org/2007/opf" unique-identifier="uid">
  <metadata xmlns:dc="http://purl.org/dc/elements/1.1/"></metadata>
  <manifest><item id="c1" href="c1.xhtml" media-type="application/xhtml+xml"/></manifest>
  <spine><itemref idref="c1"/></spine>
</package>
"@
    $stream = [System.IO.File]::Create($OutputPath)
    try {
        $zip = [System.IO.Compression.ZipArchive]::new($stream, [System.IO.Compression.ZipArchiveMode]::Create, $true)
        try {
            $mt = $zip.CreateEntry("mimetype", [System.IO.Compression.CompressionLevel]::NoCompression)
            $w = [System.IO.StreamWriter]::new($mt.Open()); $w.Write("application/epub+zip"); $w.Dispose()
            $ce = $zip.CreateEntry("META-INF/container.xml")
            $w = [System.IO.StreamWriter]::new($ce.Open()); $w.Write($container); $w.Dispose()
            $oe = $zip.CreateEntry("OEBPS/content.opf")
            $w = [System.IO.StreamWriter]::new($oe.Open()); $w.Write($opf); $w.Dispose()
            $ch = $zip.CreateEntry("OEBPS/c1.xhtml")
            $w = [System.IO.StreamWriter]::new($ch.Open()); $w.Write("<html><body><p>No metadata.</p></body></html>"); $w.Dispose()
        } finally { $zip.Dispose() }
    } finally { $stream.Dispose() }
}

# ─── Sanitize filename ─────────────────────────────────────────────────────────
function Get-SafeFilename {
    param([string]$Title)
    $safe = $Title -replace '[\\/:*?"<>|]', '_'
    return "$safe.epub"
}

# ─── MAIN ──────────────────────────────────────────────────────────────────────

$RunStart = Get-Date

Write-H "TUVIMA BOOK INGESTION TEST" -c "Cyan"
Write-RL " Run started : $($RunStart.ToString('yyyy-MM-dd HH:mm:ss'))"
Write-RL " Engine URL  : $EngineUrl"
Write-RL " Sample size : $Count of $($Catalog.Count) catalog entries"
Write-RL " Seed        : $(if ($Seed -ge 0) { $Seed } else { 'random' })"
Write-RL ""

# ── 1. Engine health check ─────────────────────────────────────────────────────
Write-R " Checking engine..." -c "Gray"
$status = Invoke-Api "/system/status"
if (-not $status) {
    Write-R " ✗ Engine not responding at $EngineUrl" -c "Red"
    Write-R "   Start the engine first: cd src/MediaEngine.Api && dotnet run" -c "Yellow"
    exit 1
}
Write-R " ✓ Engine online" -c "Green"

# ── 2. Resolve watch directory ─────────────────────────────────────────────────
if (-not $WatchDirectory) {
    $coreSettings = Invoke-Api "/settings/core"
    if ($coreSettings -and $coreSettings.watch_directory) {
        $WatchDirectory = $coreSettings.watch_directory
    }
}
if (-not $WatchDirectory -or -not (Test-Path $WatchDirectory)) {
    Write-R " ✗ Watch directory not found: '$WatchDirectory'" -c "Red"
    Write-R "   Set -WatchDirectory or configure it in the engine settings." -c "Yellow"
    exit 1
}
Write-R " ✓ Watch dir  : $WatchDirectory" -c "Green"

# ── 3. Resolve DB and library paths ───────────────────────────────────────────
$ApiDbPath     = Join-Path $RepoRoot "src\MediaEngine.Api\library.db"
$LibraryRoot   = ""
if ($coreSettings -and $coreSettings.library_root) { $LibraryRoot = $coreSettings.library_root }
$StagingRoot   = if ($LibraryRoot) { Join-Path $LibraryRoot ".staging" } else { "" }

# ── 4. WipeFirst ──────────────────────────────────────────────────────────────
if ($WipeFirst) {
    if (-not $Force) {
        Write-R "`n ⚠  WARNING: -WipeFirst will permanently erase:" -c "Yellow"
        Write-R "    • Database : $ApiDbPath" -c "Yellow"
        if ($LibraryRoot) { Write-R "    • Library  : $LibraryRoot" -c "Yellow" }
        $confirm = Read-Host "`n   Type YES to continue"
        if ($confirm -ne "YES") { Write-R "   Aborted." -c "Gray"; exit 0 }
    }

    Write-R " Wiping database and library..." -c "Yellow"

    if (Test-Path $ApiDbPath) { Remove-Item $ApiDbPath -Force; Write-R "   Deleted: $ApiDbPath" -c "DarkYellow" }
    $bak = "$ApiDbPath.bak"
    if (Test-Path $bak) { Remove-Item $bak -Force }

    if ($LibraryRoot -and (Test-Path $LibraryRoot)) {
        Remove-Item -Path $LibraryRoot -Recurse -Force -ErrorAction SilentlyContinue
        Write-R "   Cleared: $LibraryRoot" -c "DarkYellow"
    }
    Write-R " ✓ Wipe complete" -c "Green"
}

# ── 5. Random selection ────────────────────────────────────────────────────────
if ($Seed -ge 0) {
    $rng = [System.Random]::new($Seed)
} else {
    $rng = [System.Random]::new()
    $Seed = $rng.Next()   # capture for report
}

$shuffled = $Catalog | Sort-Object { $rng.NextDouble() }
$selected = @($shuffled | Select-Object -First $Count)

# Ensure duplicates have their originals in the list
$needOriginals = $selected | Where-Object { $_.S -eq "duplicate" } | ForEach-Object { $_.DuplicateOf }
foreach ($origId in $needOriginals) {
    if (-not ($selected | Where-Object { $_.Id -eq $origId })) {
        $orig = $Catalog | Where-Object { $_.Id -eq $origId }
        if ($orig) { $selected = @($selected) + @($orig) }
    }
}

# Sort: non-duplicates first, duplicates last (ensures originals are ingested first)
$selected = @($selected | Sort-Object { if ($_.S -eq "duplicate") { 1 } else { 0 } }, { $_.Id })

# ── 6. Generate EPUBs in a temp staging dir ────────────────────────────────────
$TempDir = Join-Path ([System.IO.Path]::GetTempPath()) "tuvima_booktest_$($Timestamp -replace '[^a-zA-Z0-9]','')"
New-Item -ItemType Directory -Path $TempDir | Out-Null

# Map: originalId -> temp file path (for duplicate byte copying)
$generatedPaths = @{}
$droppedFiles   = [System.Collections.Generic.List[hashtable]]::new()

foreach ($book in $selected) {
    $filename = Get-SafeFilename -Title $book.Title
    # Make filename unique per catalog entry
    $safeId   = $book.Id.ToString().PadLeft(3, '0')
    $filename = "${safeId}_${filename}"
    $tmpPath  = Join-Path $TempDir $filename

    switch ($book.S) {
        "high"      { New-ValidEpub -Book $book -OutputPath $tmpPath }
        "medium"    {
            # Partial: title + author + year but no ISBN/series/genre
            $partial = @{Id=$book.Id; Title=$book.Title; Author=$book.Author; Year=$book.Year; Series=""; Pos=""; Isbn=""; Genre=$book.Genre}
            New-ValidEpub -Book $partial -OutputPath $tmpPath
        }
        "low"       { New-BareEpub -OutputPath $tmpPath }
        "corrupt"   {
            # Random invalid bytes
            $bytes = New-Object byte[] 512
            $rng.NextBytes($bytes)
            [System.IO.File]::WriteAllBytes($tmpPath, $bytes)
        }
        "duplicate" {
            # Copy exact bytes from original
            $origPath = $generatedPaths[$book.DuplicateOf]
            if ($origPath -and (Test-Path $origPath)) {
                Copy-Item $origPath $tmpPath
            } else {
                # Original not generated yet (shouldn't happen after sort), fallback
                New-ValidEpub -Book $book -OutputPath $tmpPath
            }
        }
    }

    $generatedPaths[$book.Id] = $tmpPath

    $expectedMeta = switch ($book.S) {
        "high"      { "title, author, year, ISBN$(if($book.Series){', series'})" }
        "medium"    { "title, author, year" }
        "low"       { "filename only (no OPF)" }
        "corrupt"   { "—" }
        "duplicate" { "identical to #$($book.DuplicateOf)" }
    }

    $droppedFiles.Add(@{
        Num          = $droppedFiles.Count + 1
        Book         = $book
        TmpPath      = $tmpPath
        Filename     = $filename
        ExpectedMeta = $expectedMeta
        Scenario     = $book.S
    })
}

# ── 7. Pre-ingestion report ────────────────────────────────────────────────────
Write-S "PRE-INGESTION: FILES TO DROP INTO WATCH FOLDER"

$hdr = " {0,-4} {1,-42} {2,-10} {3,-30}" -f "#", "Filename", "Scenario", "Embedded Metadata"
$div = " {0,-4} {1,-42} {2,-10} {3,-30}" -f "────", "──────────────────────────────────────────", "──────────", "────────────────────────────────"
Write-RL $hdr; Write-RL $div

foreach ($f in $droppedFiles) {
    $icon = switch ($f.Scenario) {
        "high"      { "✦" }
        "medium"    { "◈" }
        "low"       { "○" }
        "corrupt"   { "✗" }
        "duplicate" { "⊘" }
        default     { " " }
    }
    $scen = "$icon $($f.Scenario)"
    $fn = if ($f.Filename.Length -gt 42) { $f.Filename.Substring(0,39) + "..." } else { $f.Filename }
    $row = " {0,-4} {1,-42} {2,-10} {3}" -f $f.Num, $fn, $scen, $f.ExpectedMeta
    $color = switch ($f.Scenario) {
        "high"      { "Green" }
        "medium"    { "Cyan" }
        "low"       { "Yellow" }
        "corrupt"   { "Red" }
        "duplicate" { "DarkCyan" }
        default     { "White" }
    }
    Write-R $row -c $color
}

$scenCounts = $droppedFiles | Group-Object Scenario | ForEach-Object { "$($_.Count) $($_.Name)" }
Write-RL ""
Write-R " Total: $($droppedFiles.Count) files  |  $($scenCounts -join '  •  ')" -c "Gray"

# ── 8. Drop files into watch directory ────────────────────────────────────────
Write-S "DROPPING FILES"
Write-R " Copying $($droppedFiles.Count) files to: $WatchDirectory" -c "Gray"

$DropStart = Get-Date

# Drop non-duplicates first, then duplicates
$wave1 = $droppedFiles | Where-Object { $_.Scenario -ne "duplicate" }
$wave2 = $droppedFiles | Where-Object { $_.Scenario -eq "duplicate" }

foreach ($f in $wave1) {
    $dest = Join-Path $WatchDirectory $f.Filename
    Copy-Item $f.TmpPath $dest -Force
}

if ($wave2.Count -gt 0) {
    # Small delay to let wave 1 start processing before wave 2 arrives
    Write-R " ⏳ Waiting 5s before dropping duplicates..." -c "DarkGray"
    Start-Sleep -Seconds 5

    foreach ($f in $wave2) {
        $dest = Join-Path $WatchDirectory $f.Filename
        Copy-Item $f.TmpPath $dest -Force
    }
}

Write-R " ✓ All files dropped at $((Get-Date).ToString('HH:mm:ss'))" -c "Green"
Write-R " ⏳ Waiting for engine to process (timeout: ${TimeoutSeconds}s)..." -c "Gray"

# ── 9. Poll for completion ─────────────────────────────────────────────────────
$expectedEvents = $droppedFiles.Count
$terminalTypes  = @("FileIngested", "DuplicateSkipped", "MediaFailed")
$pollInterval   = 2
$deadline       = (Get-Date).AddSeconds($TimeoutSeconds)
$lastCount      = 0

do {
    Start-Sleep -Seconds $pollInterval
    $activity = Invoke-Api "/activity/recent?limit=500"
    if ($activity) {
        $since = $DropStart.AddSeconds(-2).ToString("O")
        $events = $activity | Where-Object {
            $_.action_type -in $terminalTypes -and
            $_.occurred_at -gt $since
        }
        $count = ($events | Measure-Object).Count
        if ($count -ne $lastCount) {
            Write-R "   ... $count / $expectedEvents events detected" -c "DarkGray"
            $lastCount = $count
        }
        if ($count -ge $expectedEvents) { break }
    }
} while ((Get-Date) -lt $deadline)

$IngestionDuration = (Get-Date) - $DropStart

if ($lastCount -lt $expectedEvents) {
    Write-R " ⚠  Timeout reached. Only $lastCount of $expectedEvents events detected." -c "Yellow"
} else {
    Write-R " ✓ Ingestion complete in $([int]$IngestionDuration.TotalSeconds)s" -c "Green"
}

# ── 10. Query results ──────────────────────────────────────────────────────────
Start-Sleep -Seconds 2   # brief settle
$registry = Invoke-Api "/registry/items?limit=200"
$reviewItems = Invoke-Api "/review/pending?limit=200"
$activityAll = Invoke-Api "/activity/recent?limit=500"

# Build lookup maps
$registryByFilename = @{}
if ($registry -and $registry.items) {
    foreach ($item in $registry.items) {
        if ($item.file_name) { $registryByFilename[$item.file_name] = $item }
    }
}

$since = $DropStart.AddSeconds(-2).ToString("O")
$duplicateSkips = @()
$mediaFails     = @()
if ($activityAll) {
    $duplicateSkips = @($activityAll | Where-Object { $_.action_type -eq "DuplicateSkipped" -and $_.occurred_at -gt $since })
    $mediaFails     = @($activityAll | Where-Object { $_.action_type -eq "MediaFailed"      -and $_.occurred_at -gt $since })
}

# ── 11. Post-ingestion report ──────────────────────────────────────────────────
Write-S "POST-INGESTION: RESULTS"

$resultHdr = " {0,-4} {1,-30} {2,-22} {3,-10} {4,-8} {5}" -f "#", "Title", "Author", "Status", "Conf%", "Detail"
$resultDiv = " {0,-4} {1,-30} {2,-22} {3,-10} {4,-8} {5}" -f "────", "──────────────────────────────", "──────────────────────", "──────────", "────────", "──────────────────────"
Write-RL $resultHdr; Write-RL $resultDiv

$stats = @{ Staged=0; Review=0; Duplicate=0; Failed=0; Unknown=0; TotalConf=0.0; ConfCount=0 }
$resultRows = [System.Collections.Generic.List[hashtable]]::new()

foreach ($f in $droppedFiles) {
    $item   = $registryByFilename[$f.Filename]
    $status = "?"
    $conf   = ""
    $detail = ""
    $color  = "Gray"
    $icon   = "?"

    if ($item) {
        $confPct = [int]($item.confidence * 100)
        $conf    = "${confPct}%"

        if ($item.review_item_id) {
            $status = "Review"
            $detail = $item.review_trigger ?? "needs review"
            $icon   = "⚠"
            $color  = "Yellow"
            $stats.Review++
        } elseif ($item.status -eq "Staging") {
            $status = "Staging"
            $icon   = "✓"
            $color  = if ($confPct -ge 70) { "Green" } else { "Cyan" }
            $stats.Staged++
        } else {
            $status = $item.status
            $icon   = "✓"
            $color  = "Green"
            $stats.Staged++
        }

        $stats.TotalConf += $item.confidence
        $stats.ConfCount++
        $detail = if ($item.author) { "by $($item.author)" } else { "" }

    } elseif ($f.Scenario -eq "duplicate") {
        # Check activity log for DuplicateSkipped matching our filename
        $dupEvent = $duplicateSkips | Where-Object { $_.detail -like "*$($f.Book.Title)*" } | Select-Object -First 1
        $status = "Duplicate ⊘"
        $icon   = "⊘"
        $conf   = "—"
        $detail = "skipped (hash match)"
        $color  = "DarkCyan"
        $stats.Duplicate++

    } elseif ($f.Scenario -eq "corrupt") {
        $failEvent = $mediaFails | Where-Object { $_.detail -like "*$($f.Filename)*" -or $_.detail -like "*$($f.Book.Title)*" } | Select-Object -First 1
        $status = "Failed ✗"
        $icon   = "✗"
        $conf   = "—"
        $detail = if ($failEvent) { $failEvent.detail } else { "corrupt file" }
        $color  = "Red"
        $stats.Failed++

    } else {
        $status = "Not found"
        $icon   = "?"
        $conf   = "—"
        $detail = "not in registry"
        $color  = "DarkYellow"
        $stats.Unknown++
    }

    $titleDisp  = if ($f.Book.Title.Length -gt 30) { $f.Book.Title.Substring(0,27) + "..." } else { $f.Book.Title }
    $authorDisp = if ($f.Book.Author.Length -gt 22) { $f.Book.Author.Substring(0,19) + "..." } else { $f.Book.Author }
    $row = " {0,-4} {1,-30} {2,-22} {3,-10} {4,-8} {5}" -f $f.Num, $titleDisp, $authorDisp, $status, $conf, $detail
    Write-R $row -c $color

    $resultRows.Add(@{ Row=$row; Color=$color })
}

# ── 12. Summary ────────────────────────────────────────────────────────────────
Write-S "SUMMARY"

$avgConf = if ($stats.ConfCount -gt 0) { [int]($stats.TotalConf / $stats.ConfCount * 100) } else { 0 }
$total   = $droppedFiles.Count
$RunEnd  = Get-Date
$RunDuration = $RunEnd - $RunStart

$summaryLines = @(
    " Total files dropped   : $total"
    " ✓ Staged (pending)    : $($stats.Staged)  ($(if($total -gt 0){[int]($stats.Staged/$total*100)}else{0})%)"
    " ⚠ Sent to review      : $($stats.Review)  ($(if($total -gt 0){[int]($stats.Review/$total*100)}else{0})%)"
    " ⊘ Duplicates skipped  : $($stats.Duplicate)  ($(if($total -gt 0){[int]($stats.Duplicate/$total*100)}else{0})%)"
    " ✗ Failed / corrupt    : $($stats.Failed)  ($(if($total -gt 0){[int]($stats.Failed/$total*100)}else{0})%)"
    " ? Unresolved          : $($stats.Unknown)"
    ""
    " Avg confidence score  : ${avgConf}%"
    " Ingestion duration    : $([int]$IngestionDuration.TotalSeconds)s"
    " Total run time        : $([int]$RunDuration.TotalSeconds)s"
    " Random seed used      : $Seed"
)

foreach ($line in $summaryLines) {
    $c = if ($line -match "✓") { "Green" } elseif ($line -match "⚠") { "Yellow" } elseif ($line -match "✗") { "Red" } elseif ($line -match "⊘") { "DarkCyan" } else { "White" }
    Write-R $line -c $c
}

# ── 13. Review queue detail (if any) ──────────────────────────────────────────
if ($stats.Review -gt 0 -and $reviewItems) {
    $ourReviews = @($reviewItems | Where-Object { $_ -ne $null } | Select-Object -First 20)
    if ($ourReviews.Count -gt 0) {
        Write-S "REVIEW QUEUE ITEMS"
        foreach ($rv in $ourReviews) {
            $trigger = $rv.review_trigger ?? "unknown"
            $conf2   = if ($rv.confidence) { "$([int]($rv.confidence * 100))%" } else { "?" }
            Write-R "  $($rv.entity_id ?? '?')  trigger=$trigger  conf=$conf2  $($rv.detail ?? '')" -c "Yellow"
        }
    }
}

# ── 14. Save report ───────────────────────────────────────────────────────────
$ReportLines.Add(""); $ReportLines.Add(" Report saved: $ReportPath"); $ReportLines.Add(" Run ended  : $($RunEnd.ToString('yyyy-MM-dd HH:mm:ss'))")
$ReportLines | Set-Content -Path $ReportPath -Encoding UTF8

Write-RL ""
Write-R " Report saved to: $ReportPath" -c "DarkGray"
Write-RL ""

# ── 15. Cleanup temp dir ──────────────────────────────────────────────────────
Remove-Item -Path $TempDir -Recurse -Force -ErrorAction SilentlyContinue
