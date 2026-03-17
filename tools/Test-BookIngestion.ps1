<#
.SYNOPSIS
    Book Ingestion Test - drops synthetic EPUBs into the Tuvima watch folder
    and produces a before/after ingestion report.

.DESCRIPTION
    Generates synthetic EPUB files from a 100-title catalog (varying metadata
    quality) and drops a random selection into the watch folder. Monitors the
    engine API for completion, then queries results and writes a human-readable
    report.

    Scenarios:
      high      - Full metadata: title, author, year, ISBN, series, genre (60 books)
      medium    - Partial: title, author, year only (20 books)
      low       - No embedded metadata; engine derives title from filename (10 books)
      corrupt   - Invalid file bytes; expect MediaFailed (5 books)
      duplicate - Identical bytes to an earlier book; expect DuplicateSkipped (5 books)

.PARAMETER Count
    Number of books to randomly select from the catalog (default: 10, max: 100).

.PARAMETER Seed
    Random seed for reproducible runs. Omit for a fresh random selection.

.PARAMETER EngineUrl
    Base URL of the running Tuvima Engine API (default: http://localhost:61495).

.PARAMETER WatchDirectory
    Path to the watch folder. Overrides config auto-detection.

.PARAMETER WipeFirst
    Wipes library.db and clears library/staging folders before running.
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
    Run as "book ingestion test" - part of the Tuvima testing arsenal.
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

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

# ---------------------------------------------------------------------------
# Resolve paths
# ---------------------------------------------------------------------------
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Definition
$RepoRoot   = Split-Path -Parent $ScriptRoot
$Timestamp  = Get-Date -Format "yyyy-MM-dd_HH-mm-ss"

if (-not $ReportPath) {
    $ReportsDir = Join-Path $ScriptRoot "reports"
    if (-not (Test-Path $ReportsDir)) { New-Item -ItemType Directory -Path $ReportsDir | Out-Null }
    $ReportPath = Join-Path $ReportsDir "book-ingestion-$Timestamp.txt"
}

$ReportLines = New-Object System.Collections.Generic.List[string]

# ---------------------------------------------------------------------------
# Output helpers
# ---------------------------------------------------------------------------
$Bar72 = "=" * 72

function Write-H {
    param([string]$t, [string]$c = "Cyan")
    Write-Host $Bar72 -ForegroundColor $c
    Write-Host " $t" -ForegroundColor $c
    Write-Host $Bar72 -ForegroundColor $c
    $script:ReportLines.Add($Bar72)
    $script:ReportLines.Add(" $t")
    $script:ReportLines.Add($Bar72)
}

function Write-S {
    param([string]$t)
    $h = "`n -- $t"
    $u = " " + ("-" * 70)
    Write-Host $h -ForegroundColor Yellow
    Write-Host $u -ForegroundColor DarkYellow
    $script:ReportLines.Add("")
    $script:ReportLines.Add(" -- $t")
    $script:ReportLines.Add($u)
}

function Write-R {
    param([string]$t, [string]$c = "White")
    Write-Host $t -ForegroundColor $c
    $script:ReportLines.Add($t)
}

function Write-RL {
    param([string]$t = "")
    Write-Host $t
    $script:ReportLines.Add($t)
}

# ---------------------------------------------------------------------------
# API helper
# ---------------------------------------------------------------------------
function Invoke-Api {
    param([string]$Path, [string]$Method = "GET")
    try {
        return Invoke-RestMethod -Uri "$EngineUrl$Path" -Method $Method -TimeoutSec 10 -ErrorAction Stop
    }
    catch {
        return $null
    }
}

# ---------------------------------------------------------------------------
# Book catalog (100 entries)
# ---------------------------------------------------------------------------
$Catalog = @(
    # HIGH CONFIDENCE - full metadata (60 books)
    [pscustomobject]@{Id=1;  Title="Dune";                                 Author="Frank Herbert";       Year="1965"; Series="Dune Chronicles";         Pos="1"; Isbn="9780441013593"; Genre="Science Fiction"; S="high"}
    [pscustomobject]@{Id=2;  Title="Foundation";                           Author="Isaac Asimov";        Year="1951"; Series="Foundation";              Pos="1"; Isbn="9780553293357"; Genre="Science Fiction"; S="high"}
    [pscustomobject]@{Id=3;  Title="Leviathan Wakes";                      Author="James S.A. Corey";    Year="2011"; Series="The Expanse";             Pos="1"; Isbn="9780316129084"; Genre="Science Fiction"; S="high"}
    [pscustomobject]@{Id=4;  Title="The Name of the Wind";                 Author="Patrick Rothfuss";    Year="2007"; Series="Kingkiller Chronicle";    Pos="1"; Isbn="9780756404079"; Genre="Fantasy";         S="high"}
    [pscustomobject]@{Id=5;  Title="A Game of Thrones";                    Author="George R.R. Martin";  Year="1996"; Series="A Song of Ice and Fire"; Pos="1"; Isbn="9780553381689"; Genre="Fantasy";         S="high"}
    [pscustomobject]@{Id=6;  Title="The Way of Kings";                     Author="Brandon Sanderson";   Year="2010"; Series="The Stormlight Archive"; Pos="1"; Isbn="9780765326355"; Genre="Fantasy";         S="high"}
    [pscustomobject]@{Id=7;  Title="Mistborn: The Final Empire";           Author="Brandon Sanderson";   Year="2006"; Series="Mistborn";               Pos="1"; Isbn="9780765311788"; Genre="Fantasy";         S="high"}
    [pscustomobject]@{Id=8;  Title="The Hitchhiker's Guide to the Galaxy"; Author="Douglas Adams";       Year="1979"; Series="Hitchhiker's Guide";     Pos="1"; Isbn="9780345391803"; Genre="Comedy";          S="high"}
    [pscustomobject]@{Id=9;  Title="Neuromancer";                          Author="William Gibson";      Year="1984"; Series="";                        Pos="";  Isbn="9780441569595"; Genre="Cyberpunk";       S="high"}
    [pscustomobject]@{Id=10; Title="Ender's Game";                         Author="Orson Scott Card";    Year="1985"; Series="Ender's Game";            Pos="1"; Isbn="9780312853235"; Genre="Science Fiction"; S="high"}
    [pscustomobject]@{Id=11; Title="The Lord of the Rings";                Author="J.R.R. Tolkien";      Year="1954"; Series="Middle-earth";            Pos="1"; Isbn="9780261102354"; Genre="Fantasy";         S="high"}
    [pscustomobject]@{Id=12; Title="1984";                                 Author="George Orwell";       Year="1949"; Series="";                        Pos="";  Isbn="9780451524935"; Genre="Dystopian";       S="high"}
    [pscustomobject]@{Id=13; Title="Brave New World";                      Author="Aldous Huxley";       Year="1932"; Series="";                        Pos="";  Isbn="9780060850524"; Genre="Dystopian";       S="high"}
    [pscustomobject]@{Id=14; Title="The Martian";                          Author="Andy Weir";           Year="2011"; Series="";                        Pos="";  Isbn="9780553418026"; Genre="Science Fiction"; S="high"}
    [pscustomobject]@{Id=15; Title="Project Hail Mary";                    Author="Andy Weir";           Year="2021"; Series="";                        Pos="";  Isbn="9780593135204"; Genre="Science Fiction"; S="high"}
    [pscustomobject]@{Id=16; Title="Red Rising";                           Author="Pierce Brown";        Year="2014"; Series="Red Rising Saga";         Pos="1"; Isbn="9780345539786"; Genre="Science Fiction"; S="high"}
    [pscustomobject]@{Id=17; Title="The Blade Itself";                     Author="Joe Abercrombie";     Year="2006"; Series="The First Law";           Pos="1"; Isbn="9780575079793"; Genre="Fantasy";         S="high"}
    [pscustomobject]@{Id=18; Title="Words of Radiance";                    Author="Brandon Sanderson";   Year="2014"; Series="The Stormlight Archive"; Pos="2"; Isbn="9780765326362"; Genre="Fantasy";         S="high"}
    [pscustomobject]@{Id=19; Title="Caliban's War";                        Author="James S.A. Corey";    Year="2012"; Series="The Expanse";             Pos="2"; Isbn="9780316202107"; Genre="Science Fiction"; S="high"}
    [pscustomobject]@{Id=20; Title="Dune Messiah";                         Author="Frank Herbert";       Year="1969"; Series="Dune Chronicles";         Pos="2"; Isbn="9780441172696"; Genre="Science Fiction"; S="high"}
    [pscustomobject]@{Id=21; Title="Foundation and Empire";                Author="Isaac Asimov";        Year="1952"; Series="Foundation";              Pos="2"; Isbn="9780553293371"; Genre="Science Fiction"; S="high"}
    [pscustomobject]@{Id=22; Title="The Well of Ascension";                Author="Brandon Sanderson";   Year="2007"; Series="Mistborn";               Pos="2"; Isbn="9780765316882"; Genre="Fantasy";         S="high"}
    [pscustomobject]@{Id=23; Title="The Eye of the World";                 Author="Robert Jordan";       Year="1990"; Series="The Wheel of Time";      Pos="1"; Isbn="9780765334343"; Genre="Fantasy";         S="high"}
    [pscustomobject]@{Id=24; Title="The Great Hunt";                       Author="Robert Jordan";       Year="1990"; Series="The Wheel of Time";      Pos="2"; Isbn="9780765334350"; Genre="Fantasy";         S="high"}
    [pscustomobject]@{Id=25; Title="A Wizard of Earthsea";                 Author="Ursula K. Le Guin";   Year="1968"; Series="Earthsea Cycle";         Pos="1"; Isbn="9780553383041"; Genre="Fantasy";         S="high"}
    [pscustomobject]@{Id=26; Title="The Left Hand of Darkness";            Author="Ursula K. Le Guin";   Year="1969"; Series="Hainish Cycle";          Pos="";  Isbn="9780441478125"; Genre="Science Fiction"; S="high"}
    [pscustomobject]@{Id=27; Title="Flowers for Algernon";                 Author="Daniel Keyes";        Year="1966"; Series="";                        Pos="";  Isbn="9780156030304"; Genre="Science Fiction"; S="high"}
    [pscustomobject]@{Id=28; Title="Slaughterhouse-Five";                  Author="Kurt Vonnegut";       Year="1969"; Series="";                        Pos="";  Isbn="9780440180296"; Genre="Science Fiction"; S="high"}
    [pscustomobject]@{Id=29; Title="The Hobbit";                           Author="J.R.R. Tolkien";      Year="1937"; Series="Middle-earth";            Pos="";  Isbn="9780547928227"; Genre="Fantasy";         S="high"}
    [pscustomobject]@{Id=30; Title="The Color of Magic";                   Author="Terry Pratchett";     Year="1983"; Series="Discworld";              Pos="1"; Isbn="9780062225672"; Genre="Fantasy Comedy";  S="high"}
    [pscustomobject]@{Id=31; Title="Good Omens";                           Author="Terry Pratchett";     Year="1990"; Series="";                        Pos="";  Isbn="9780060853983"; Genre="Fantasy Comedy";  S="high"}
    [pscustomobject]@{Id=32; Title="American Gods";                        Author="Neil Gaiman";         Year="2001"; Series="";                        Pos="";  Isbn="9780380973651"; Genre="Fantasy";         S="high"}
    [pscustomobject]@{Id=33; Title="The Lies of Locke Lamora";             Author="Scott Lynch";         Year="2006"; Series="Gentleman Bastard";      Pos="1"; Isbn="9780553588941"; Genre="Fantasy";         S="high"}
    [pscustomobject]@{Id=34; Title="Assassin's Apprentice";                Author="Robin Hobb";          Year="1995"; Series="Farseer Trilogy";        Pos="1"; Isbn="9780553573398"; Genre="Fantasy";         S="high"}
    [pscustomobject]@{Id=35; Title="Ancillary Justice";                    Author="Ann Leckie";          Year="2013"; Series="Imperial Radch";         Pos="1"; Isbn="9780316246620"; Genre="Science Fiction"; S="high"}
    [pscustomobject]@{Id=36; Title="The Dispossessed";                     Author="Ursula K. Le Guin";   Year="1974"; Series="Hainish Cycle";          Pos="";  Isbn="9780061054884"; Genre="Science Fiction"; S="high"}
    [pscustomobject]@{Id=37; Title="Hyperion";                             Author="Dan Simmons";         Year="1989"; Series="Hyperion Cantos";        Pos="1"; Isbn="9780553283686"; Genre="Science Fiction"; S="high"}
    [pscustomobject]@{Id=38; Title="The Fall of Hyperion";                 Author="Dan Simmons";         Year="1990"; Series="Hyperion Cantos";        Pos="2"; Isbn="9780553288208"; Genre="Science Fiction"; S="high"}
    [pscustomobject]@{Id=39; Title="Snow Crash";                           Author="Neal Stephenson";     Year="1992"; Series="";                        Pos="";  Isbn="9780553380958"; Genre="Cyberpunk";       S="high"}
    [pscustomobject]@{Id=40; Title="Cryptonomicon";                        Author="Neal Stephenson";     Year="1999"; Series="";                        Pos="";  Isbn="9780060512804"; Genre="Thriller";        S="high"}
    [pscustomobject]@{Id=41; Title="Old Man's War";                        Author="John Scalzi";         Year="2005"; Series="Old Man's War";          Pos="1"; Isbn="9780765315034"; Genre="Science Fiction"; S="high"}
    [pscustomobject]@{Id=42; Title="The Android's Dream";                  Author="John Scalzi";         Year="2006"; Series="";                        Pos="";  Isbn="9780765348494"; Genre="Science Fiction"; S="high"}
    [pscustomobject]@{Id=43; Title="Piranesi";                             Author="Susanna Clarke";      Year="2020"; Series="";                        Pos="";  Isbn="9781635575637"; Genre="Fantasy";         S="high"}
    [pscustomobject]@{Id=44; Title="The Fifth Season";                     Author="N.K. Jemisin";        Year="2015"; Series="The Broken Earth";       Pos="1"; Isbn="9780316229296"; Genre="Science Fiction"; S="high"}
    [pscustomobject]@{Id=45; Title="All Systems Red";                      Author="Martha Wells";        Year="2017"; Series="Murderbot Diaries";      Pos="1"; Isbn="9780765397539"; Genre="Science Fiction"; S="high"}
    [pscustomobject]@{Id=46; Title="A Memory Called Empire";               Author="Arkady Martine";      Year="2019"; Series="Teixcalaan";             Pos="1"; Isbn="9781250186430"; Genre="Science Fiction"; S="high"}
    [pscustomobject]@{Id=47; Title="The Long Way to a Small Angry Planet"; Author="Becky Chambers";      Year="2014"; Series="Wayfarers";             Pos="1"; Isbn="9781473619814"; Genre="Science Fiction"; S="high"}
    [pscustomobject]@{Id=48; Title="Klara and the Sun";                    Author="Kazuo Ishiguro";      Year="2021"; Series="";                        Pos="";  Isbn="9780593311295"; Genre="Science Fiction"; S="high"}
    [pscustomobject]@{Id=49; Title="The Buried Giant";                     Author="Kazuo Ishiguro";      Year="2015"; Series="";                        Pos="";  Isbn="9780307455796"; Genre="Fantasy";         S="high"}
    [pscustomobject]@{Id=50; Title="Jonathan Strange and Mr Norrell";      Author="Susanna Clarke";      Year="2004"; Series="";                        Pos="";  Isbn="9781582344164"; Genre="Fantasy";         S="high"}
    [pscustomobject]@{Id=51; Title="Children of Time";                     Author="Adrian Tchaikovsky";  Year="2015"; Series="Children of Time";       Pos="1"; Isbn="9781447273288"; Genre="Science Fiction"; S="high"}
    [pscustomobject]@{Id=52; Title="To Be Taught If Fortunate";            Author="Becky Chambers";      Year="2020"; Series="";                        Pos="";  Isbn="9781250236234"; Genre="Science Fiction"; S="high"}
    [pscustomobject]@{Id=53; Title="The Ninth Rain";                       Author="Jen Williams";        Year="2017"; Series="The Winnowing Flame";    Pos="1"; Isbn="9781472235299"; Genre="Fantasy";         S="high"}
    [pscustomobject]@{Id=54; Title="Abaddon's Gate";                       Author="James S.A. Corey";    Year="2013"; Series="The Expanse";             Pos="3"; Isbn="9780316129060"; Genre="Science Fiction"; S="high"}
    [pscustomobject]@{Id=55; Title="Second Foundation";                    Author="Isaac Asimov";        Year="1953"; Series="Foundation";              Pos="3"; Isbn="9780553293388"; Genre="Science Fiction"; S="high"}
    [pscustomobject]@{Id=56; Title="Children of Dune";                     Author="Frank Herbert";       Year="1976"; Series="Dune Chronicles";         Pos="3"; Isbn="9780441104024"; Genre="Science Fiction"; S="high"}
    [pscustomobject]@{Id=57; Title="The Hero of Ages";                     Author="Brandon Sanderson";   Year="2008"; Series="Mistborn";               Pos="3"; Isbn="9780765356147"; Genre="Fantasy";         S="high"}
    [pscustomobject]@{Id=58; Title="Oathbringer";                          Author="Brandon Sanderson";   Year="2017"; Series="The Stormlight Archive"; Pos="3"; Isbn="9780765326379"; Genre="Fantasy";         S="high"}
    [pscustomobject]@{Id=59; Title="The Dragon Reborn";                    Author="Robert Jordan";       Year="1991"; Series="The Wheel of Time";      Pos="3"; Isbn="9780765334367"; Genre="Fantasy";         S="high"}
    [pscustomobject]@{Id=60; Title="Before They Are Hanged";               Author="Joe Abercrombie";     Year="2007"; Series="The First Law";           Pos="2"; Isbn="9781591025788"; Genre="Fantasy";         S="high"}
    # MEDIUM CONFIDENCE - title + author + year (20 books)
    [pscustomobject]@{Id=61; Title="The Alchemist";               Author="Paulo Coelho";        Year="1988"; Series=""; Pos=""; Isbn=""; Genre=""; S="medium"}
    [pscustomobject]@{Id=62; Title="Siddhartha";                  Author="Hermann Hesse";       Year="1922"; Series=""; Pos=""; Isbn=""; Genre=""; S="medium"}
    [pscustomobject]@{Id=63; Title="The Trial";                   Author="Franz Kafka";         Year="1925"; Series=""; Pos=""; Isbn=""; Genre=""; S="medium"}
    [pscustomobject]@{Id=64; Title="Invisible Man";               Author="Ralph Ellison";       Year="1952"; Series=""; Pos=""; Isbn=""; Genre=""; S="medium"}
    [pscustomobject]@{Id=65; Title="The Bell Jar";                Author="Sylvia Plath";        Year="1963"; Series=""; Pos=""; Isbn=""; Genre=""; S="medium"}
    [pscustomobject]@{Id=66; Title="A Farewell to Arms";          Author="Ernest Hemingway";    Year="1929"; Series=""; Pos=""; Isbn=""; Genre=""; S="medium"}
    [pscustomobject]@{Id=67; Title="For Whom the Bell Tolls";     Author="Ernest Hemingway";    Year="1940"; Series=""; Pos=""; Isbn=""; Genre=""; S="medium"}
    [pscustomobject]@{Id=68; Title="The Old Man and the Sea";     Author="Ernest Hemingway";    Year="1952"; Series=""; Pos=""; Isbn=""; Genre=""; S="medium"}
    [pscustomobject]@{Id=69; Title="Of Mice and Men";             Author="John Steinbeck";      Year="1937"; Series=""; Pos=""; Isbn=""; Genre=""; S="medium"}
    [pscustomobject]@{Id=70; Title="East of Eden";                Author="John Steinbeck";      Year="1952"; Series=""; Pos=""; Isbn=""; Genre=""; S="medium"}
    [pscustomobject]@{Id=71; Title="Moby Dick";                   Author="Herman Melville";     Year="1851"; Series=""; Pos=""; Isbn=""; Genre=""; S="medium"}
    [pscustomobject]@{Id=72; Title="Crime and Punishment";        Author="Fyodor Dostoevsky";   Year="1866"; Series=""; Pos=""; Isbn=""; Genre=""; S="medium"}
    [pscustomobject]@{Id=73; Title="The Brothers Karamazov";      Author="Fyodor Dostoevsky";   Year="1879"; Series=""; Pos=""; Isbn=""; Genre=""; S="medium"}
    [pscustomobject]@{Id=74; Title="War and Peace";               Author="Leo Tolstoy";         Year="1869"; Series=""; Pos=""; Isbn=""; Genre=""; S="medium"}
    [pscustomobject]@{Id=75; Title="Anna Karenina";               Author="Leo Tolstoy";         Year="1877"; Series=""; Pos=""; Isbn=""; Genre=""; S="medium"}
    [pscustomobject]@{Id=76; Title="Don Quixote";                 Author="Miguel de Cervantes"; Year="1605"; Series=""; Pos=""; Isbn=""; Genre=""; S="medium"}
    [pscustomobject]@{Id=77; Title="The Divine Comedy";           Author="Dante Alighieri";     Year="1320"; Series=""; Pos=""; Isbn=""; Genre=""; S="medium"}
    [pscustomobject]@{Id=78; Title="Les Miserables";              Author="Victor Hugo";         Year="1862"; Series=""; Pos=""; Isbn=""; Genre=""; S="medium"}
    [pscustomobject]@{Id=79; Title="The Count of Monte Cristo";   Author="Alexandre Dumas";     Year="1844"; Series=""; Pos=""; Isbn=""; Genre=""; S="medium"}
    [pscustomobject]@{Id=80; Title="Around the World in 80 Days"; Author="Jules Verne";         Year="1872"; Series=""; Pos=""; Isbn=""; Genre=""; S="medium"}
    # LOW CONFIDENCE - no embedded metadata (10 books)
    [pscustomobject]@{Id=81; Title="unlabeled_scan_001";          Author=""; Year=""; Series=""; Pos=""; Isbn=""; Genre=""; S="low"}
    [pscustomobject]@{Id=82; Title="ebook_download_final";        Author=""; Year=""; Series=""; Pos=""; Isbn=""; Genre=""; S="low"}
    [pscustomobject]@{Id=83; Title="converted_doc_v2";            Author=""; Year=""; Series=""; Pos=""; Isbn=""; Genre=""; S="low"}
    [pscustomobject]@{Id=84; Title="reading_list_item_3";         Author=""; Year=""; Series=""; Pos=""; Isbn=""; Genre=""; S="low"}
    [pscustomobject]@{Id=85; Title="backup_book_copy";            Author=""; Year=""; Series=""; Pos=""; Isbn=""; Genre=""; S="low"}
    [pscustomobject]@{Id=86; Title="mystery_epub_untitled";       Author=""; Year=""; Series=""; Pos=""; Isbn=""; Genre=""; S="low"}
    [pscustomobject]@{Id=87; Title="document_export_003";         Author=""; Year=""; Series=""; Pos=""; Isbn=""; Genre=""; S="low"}
    [pscustomobject]@{Id=88; Title="temp_file_do_not_delete";     Author=""; Year=""; Series=""; Pos=""; Isbn=""; Genre=""; S="low"}
    [pscustomobject]@{Id=89; Title="new_book_no_title";           Author=""; Year=""; Series=""; Pos=""; Isbn=""; Genre=""; S="low"}
    [pscustomobject]@{Id=90; Title="archive_entry_2024";          Author=""; Year=""; Series=""; Pos=""; Isbn=""; Genre=""; S="low"}
    # CORRUPT - invalid file bytes (5 books)
    [pscustomobject]@{Id=91; Title="corrupted_epub_A"; Author=""; Year=""; Series=""; Pos=""; Isbn=""; Genre=""; S="corrupt"}
    [pscustomobject]@{Id=92; Title="corrupted_epub_B"; Author=""; Year=""; Series=""; Pos=""; Isbn=""; Genre=""; S="corrupt"}
    [pscustomobject]@{Id=93; Title="truncated_file";   Author=""; Year=""; Series=""; Pos=""; Isbn=""; Genre=""; S="corrupt"}
    [pscustomobject]@{Id=94; Title="wrong_magic_bytes";Author=""; Year=""; Series=""; Pos=""; Isbn=""; Genre=""; S="corrupt"}
    [pscustomobject]@{Id=95; Title="empty_epub";       Author=""; Year=""; Series=""; Pos=""; Isbn=""; Genre=""; S="corrupt"}
    # DUPLICATES - identical bytes to books 1-5 (5 books)
    [pscustomobject]@{Id=96;  Title="Dune";            Author="Frank Herbert";    Year="1965"; Series="Dune Chronicles"; Pos="1"; Isbn="9780441013593"; Genre="Science Fiction"; S="duplicate"; DuplicateOf=1}
    [pscustomobject]@{Id=97;  Title="Foundation";      Author="Isaac Asimov";     Year="1951"; Series="Foundation";      Pos="1"; Isbn="9780553293357"; Genre="Science Fiction"; S="duplicate"; DuplicateOf=2}
    [pscustomobject]@{Id=98;  Title="Leviathan Wakes"; Author="James S.A. Corey"; Year="2011"; Series="The Expanse";     Pos="1"; Isbn="9780316129084"; Genre="Science Fiction"; S="duplicate"; DuplicateOf=3}
    [pscustomobject]@{Id=99;  Title="The Martian";     Author="Andy Weir";        Year="2011"; Series="";               Pos="";  Isbn="9780553418026"; Genre="Science Fiction"; S="duplicate"; DuplicateOf=14}
    [pscustomobject]@{Id=100; Title="1984";             Author="George Orwell";    Year="1949"; Series="";               Pos="";  Isbn="9780451524935"; Genre="Dystopian";       S="duplicate"; DuplicateOf=12}
)

# ---------------------------------------------------------------------------
# EPUB generation
# ---------------------------------------------------------------------------
function New-ValidEpub {
    param([object]$Book, [string]$OutputPath)

    function Escape-Xml([string]$s) {
        return $s.Replace("&","&amp;").Replace("<","&lt;").Replace(">","&gt;").Replace('"',"&quot;").Replace("'","&apos;")
    }

    $et  = Escape-Xml $Book.Title
    $ea  = Escape-Xml $Book.Author
    $seriesmeta = if ($Book.Series) { "    <meta property=`"belongs-to-collection`" id=`"series`">$(Escape-Xml $Book.Series)</meta>`n    <meta property=`"group-position`">$($Book.Pos)</meta>" } else { "" }
    $genremeta  = if ($Book.Genre)  { "    <dc:subject>$(Escape-Xml $Book.Genre)</dc:subject>" } else { "" }
    $isbnid     = if ($Book.Isbn)   { "urn:isbn:$($Book.Isbn)" } else { "urn:uuid:$(([System.Guid]::NewGuid()).ToString())" }

    $container = "<?xml version=`"1.0`" encoding=`"UTF-8`"?>`n<container version=`"1.0`" xmlns=`"urn:oasis:names:tc:opendocument:xmlns:container`">`n  <rootfiles>`n    <rootfile full-path=`"OEBPS/content.opf`" media-type=`"application/oebps-package+xml`"/>`n  </rootfiles>`n</container>"

    $opf = "<?xml version=`"1.0`" encoding=`"UTF-8`"?>`n<package version=`"3.0`" xmlns=`"http://www.idpf.org/2007/opf`" unique-identifier=`"uid`">`n  <metadata xmlns:dc=`"http://purl.org/dc/elements/1.1/`" xmlns:opf=`"http://www.idpf.org/2007/opf`">`n    <dc:identifier id=`"uid`">$isbnid</dc:identifier>`n    <dc:title>$et</dc:title>`n    <dc:creator opf:role=`"aut`">$ea</dc:creator>`n    <dc:date>$($Book.Year)</dc:date>`n    <dc:language>en</dc:language>`n    $seriesmeta`n    $genremeta`n  </metadata>`n  <manifest>`n    <item id=`"c1`" href=`"chapter1.xhtml`" media-type=`"application/xhtml+xml`"/>`n  </manifest>`n  <spine><itemref idref=`"c1`"/></spine>`n</package>"

    $chapter = "<?xml version=`"1.0`" encoding=`"utf-8`"?><!DOCTYPE html><html xmlns=`"http://www.w3.org/1999/xhtml`"><head><title>$et</title></head><body><p>Synthetic test content for $et by $ea.</p></body></html>"

    $stream = [System.IO.File]::Create($OutputPath)
    try {
        $zip = New-Object System.IO.Compression.ZipArchive($stream, [System.IO.Compression.ZipArchiveMode]::Create, $true)
        try {
            $e = $zip.CreateEntry("mimetype", [System.IO.Compression.CompressionLevel]::NoCompression)
            $w = New-Object System.IO.StreamWriter($e.Open()); $w.Write("application/epub+zip"); $w.Dispose()
            $e = $zip.CreateEntry("META-INF/container.xml")
            $w = New-Object System.IO.StreamWriter($e.Open()); $w.Write($container); $w.Dispose()
            $e = $zip.CreateEntry("OEBPS/content.opf")
            $w = New-Object System.IO.StreamWriter($e.Open()); $w.Write($opf); $w.Dispose()
            $e = $zip.CreateEntry("OEBPS/chapter1.xhtml")
            $w = New-Object System.IO.StreamWriter($e.Open()); $w.Write($chapter); $w.Dispose()
        }
        finally { $zip.Dispose() }
    }
    finally { $stream.Dispose() }
}

function New-BareEpub {
    param([string]$OutputPath)
    $container = "<?xml version=`"1.0`" encoding=`"UTF-8`"?><container version=`"1.0`" xmlns=`"urn:oasis:names:tc:opendocument:xmlns:container`"><rootfiles><rootfile full-path=`"OEBPS/content.opf`" media-type=`"application/oebps-package+xml`"/></rootfiles></container>"
    $opf       = "<?xml version=`"1.0`" encoding=`"UTF-8`"?><package version=`"3.0`" xmlns=`"http://www.idpf.org/2007/opf`" unique-identifier=`"uid`"><metadata xmlns:dc=`"http://purl.org/dc/elements/1.1/`"></metadata><manifest><item id=`"c1`" href=`"c1.xhtml`" media-type=`"application/xhtml+xml`"/></manifest><spine><itemref idref=`"c1`"/></spine></package>"
    $stream = [System.IO.File]::Create($OutputPath)
    try {
        $zip = New-Object System.IO.Compression.ZipArchive($stream, [System.IO.Compression.ZipArchiveMode]::Create, $true)
        try {
            $e = $zip.CreateEntry("mimetype", [System.IO.Compression.CompressionLevel]::NoCompression)
            $w = New-Object System.IO.StreamWriter($e.Open()); $w.Write("application/epub+zip"); $w.Dispose()
            $e = $zip.CreateEntry("META-INF/container.xml")
            $w = New-Object System.IO.StreamWriter($e.Open()); $w.Write($container); $w.Dispose()
            $e = $zip.CreateEntry("OEBPS/content.opf")
            $w = New-Object System.IO.StreamWriter($e.Open()); $w.Write($opf); $w.Dispose()
            $e = $zip.CreateEntry("OEBPS/c1.xhtml")
            $w = New-Object System.IO.StreamWriter($e.Open()); $w.Write("<html><body><p>No metadata.</p></body></html>"); $w.Dispose()
        }
        finally { $zip.Dispose() }
    }
    finally { $stream.Dispose() }
}

function Get-SafeFilename {
    param([string]$Title)
    $safe = $Title -replace '[\\/:*?"<>|]', '_'
    return "$safe.epub"
}

# ===========================================================================
# MAIN
# ===========================================================================

$RunStart = Get-Date

Write-H "TUVIMA BOOK INGESTION TEST"
Write-RL " Run started : $($RunStart.ToString('yyyy-MM-dd HH:mm:ss'))"
Write-RL " Engine URL  : $EngineUrl"
Write-RL " Sample size : $Count of $($Catalog.Count) catalog entries"
Write-RL " Seed        : $(if ($Seed -ge 0) { $Seed } else { 'random' })"
Write-RL ""

# -- 1. Engine health check --------------------------------------------------
Write-R " Checking engine..." -c "Gray"
$status = Invoke-Api "/system/status"
if (-not $status) {
    Write-R " ENGINE NOT RESPONDING at $EngineUrl" -c "Red"
    Write-R "   Start the engine first: cd src/MediaEngine.Api && dotnet run" -c "Yellow"
    exit 1
}
Write-R " Engine online" -c "Green"

# -- 2. Resolve watch directory ----------------------------------------------
if (-not $WatchDirectory) {
    $coreSettings = Invoke-Api "/settings/core"
    if ($coreSettings -and $coreSettings.watch_directory) {
        $WatchDirectory = $coreSettings.watch_directory
    }
}
if (-not $WatchDirectory -or -not (Test-Path $WatchDirectory)) {
    Write-R " Watch directory not found: '$WatchDirectory'" -c "Red"
    Write-R "   Set -WatchDirectory or configure it in the engine settings." -c "Yellow"
    exit 1
}
Write-R " Watch dir   : $WatchDirectory" -c "Green"

# -- 3. Resolve DB and library paths -----------------------------------------
$ApiDbPath   = Join-Path $RepoRoot "src\MediaEngine.Api\library.db"
$LibraryRoot = ""
if ($null -ne $coreSettings -and $coreSettings.library_root) { $LibraryRoot = $coreSettings.library_root }

# -- 4. WipeFirst ------------------------------------------------------------
if ($WipeFirst) {
    if (-not $Force) {
        Write-R ""
        Write-R " WARNING: -WipeFirst will permanently erase:" -c "Yellow"
        Write-R "   Database : $ApiDbPath" -c "Yellow"
        if ($LibraryRoot) { Write-R "   Library  : $LibraryRoot" -c "Yellow" }
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
    Write-R " Wipe complete" -c "Green"
}

# -- 5. Random selection -----------------------------------------------------
if ($Seed -ge 0) {
    $rng = New-Object System.Random($Seed)
} else {
    $rng = New-Object System.Random
    $Seed = $rng.Next()
}

$shuffled = $Catalog | Sort-Object { $rng.NextDouble() }
$selected = @($shuffled | Select-Object -First $Count)

# Ensure duplicates have their originals in the list
foreach ($dup in ($selected | Where-Object { $_.S -eq "duplicate" })) {
    $origId = $dup.DuplicateOf
    if (-not ($selected | Where-Object { $_.Id -eq $origId })) {
        $orig = $Catalog | Where-Object { $_.Id -eq $origId }
        if ($orig) { $selected = @($selected) + @($orig) }
    }
}

# Sort: non-duplicates first (ensures originals ingested before duplicates)
$selected = @($selected | Sort-Object @{Expression={if($_.S -eq "duplicate"){1}else{0}}}, @{Expression={$_.Id}})

# -- 6. Generate EPUBs in temp dir -------------------------------------------
$TempDir = Join-Path ([System.IO.Path]::GetTempPath()) "tuvima_booktest_$Timestamp"
New-Item -ItemType Directory -Path $TempDir | Out-Null

$generatedPaths = @{}
$droppedFiles   = New-Object System.Collections.Generic.List[object]

foreach ($book in $selected) {
    $safeId   = $book.Id.ToString().PadLeft(3, '0')
    $safeName = Get-SafeFilename -Title $book.Title
    $filename = "${safeId}_${safeName}"
    $tmpPath  = Join-Path $TempDir $filename

    switch ($book.S) {
        "high" {
            New-ValidEpub -Book $book -OutputPath $tmpPath
        }
        "medium" {
            $partial = [pscustomobject]@{Title=$book.Title; Author=$book.Author; Year=$book.Year; Series=""; Pos=""; Isbn=""; Genre=""}
            New-ValidEpub -Book $partial -OutputPath $tmpPath
        }
        "low" {
            New-BareEpub -OutputPath $tmpPath
        }
        "corrupt" {
            $bytes = New-Object byte[] 512
            $rng.NextBytes($bytes)
            [System.IO.File]::WriteAllBytes($tmpPath, $bytes)
        }
        "duplicate" {
            $origPath = $generatedPaths[$book.DuplicateOf]
            if ($origPath -and (Test-Path $origPath)) {
                Copy-Item $origPath $tmpPath
            } else {
                New-ValidEpub -Book $book -OutputPath $tmpPath
            }
        }
    }

    $generatedPaths[$book.Id] = $tmpPath

    $meta = switch ($book.S) {
        "high"      { "title, author, year, ISBN$(if($book.Series){', series'})" }
        "medium"    { "title, author, year" }
        "low"       { "filename only (no OPF)" }
        "corrupt"   { "(invalid bytes)" }
        "duplicate" { "identical to book #$($book.DuplicateOf)" }
        default     { "?" }
    }

    $droppedFiles.Add([pscustomobject]@{
        Num      = $droppedFiles.Count + 1
        Book     = $book
        TmpPath  = $tmpPath
        Filename = $filename
        Meta     = $meta
        Scenario = $book.S
    })
}

# -- 7. Pre-ingestion report -------------------------------------------------
Write-S "PRE-INGESTION: FILES TO DROP INTO WATCH FOLDER"

$hdr = " {0,-4} {1,-44} {2,-10} {3}" -f "#", "Filename", "Scenario", "Embedded Metadata"
$div = " {0,-4} {1,-44} {2,-10} {3}" -f "----", "--------------------------------------------", "----------", "-----------------------------"
Write-RL $hdr; Write-RL $div

foreach ($f in $droppedFiles) {
    $fn  = if ($f.Filename.Length -gt 44) { $f.Filename.Substring(0,41) + "..." } else { $f.Filename }
    $row = " {0,-4} {1,-44} {2,-10} {3}" -f $f.Num, $fn, $f.Scenario, $f.Meta
    $c   = switch ($f.Scenario) {
        "high"      { "Green" }
        "medium"    { "Cyan" }
        "low"       { "Yellow" }
        "corrupt"   { "Red" }
        "duplicate" { "DarkCyan" }
        default     { "White" }
    }
    Write-R $row -c $c
}

$scenCounts = @($droppedFiles | Group-Object Scenario | ForEach-Object { "$($_.Count) $($_.Name)" })
Write-RL ""
Write-R " Total: $($droppedFiles.Count) files  |  $($scenCounts -join '  /  ')" -c "Gray"

# -- 8. Drop files -----------------------------------------------------------
Write-S "DROPPING FILES"
Write-R " Copying $($droppedFiles.Count) files to: $WatchDirectory" -c "Gray"

$DropStart = Get-Date

$wave1 = @($droppedFiles | Where-Object { $_.Scenario -ne "duplicate" })
$wave2 = @($droppedFiles | Where-Object { $_.Scenario -eq "duplicate" })

foreach ($f in $wave1) {
    Copy-Item $f.TmpPath (Join-Path $WatchDirectory $f.Filename) -Force
}

if ($wave2.Count -gt 0) {
    Write-R " Waiting 5s before dropping duplicates..." -c "DarkGray"
    Start-Sleep -Seconds 5
    foreach ($f in $wave2) {
        Copy-Item $f.TmpPath (Join-Path $WatchDirectory $f.Filename) -Force
    }
}

Write-R " All $($droppedFiles.Count) files dropped at $((Get-Date).ToString('HH:mm:ss'))" -c "Green"
Write-R " Waiting for engine (timeout: ${TimeoutSeconds}s)..." -c "Gray"

# -- 9. Poll for completion --------------------------------------------------
$expectedEvents = $droppedFiles.Count
$terminalTypes  = @("FileIngested", "DuplicateSkipped", "MediaFailed")
$deadline       = (Get-Date).AddSeconds($TimeoutSeconds)
$lastCount      = 0

do {
    Start-Sleep -Seconds 2
    $activity = Invoke-Api "/activity/recent?limit=500"
    if ($activity) {
        $sinceStr = $DropStart.AddSeconds(-2).ToString("O")
        $events   = @($activity | Where-Object { $_.action_type -in $terminalTypes -and $_.occurred_at -gt $sinceStr })
        $cnt      = $events.Count
        if ($cnt -ne $lastCount) {
            Write-R "   ... $cnt / $expectedEvents events detected" -c "DarkGray"
            $lastCount = $cnt
        }
        if ($cnt -ge $expectedEvents) { break }
    }
} while ((Get-Date) -lt $deadline)

$IngDuration = (Get-Date) - $DropStart

if ($lastCount -lt $expectedEvents) {
    Write-R " Timeout reached. $lastCount of $expectedEvents events detected." -c "Yellow"
} else {
    Write-R " Ingestion complete in $([int]$IngDuration.TotalSeconds)s" -c "Green"
}

# -- 10. Query results -------------------------------------------------------
Start-Sleep -Seconds 2
$registry    = Invoke-Api "/registry/items?limit=200"
$reviewItems = Invoke-Api "/review/pending?limit=200"
$actAll      = Invoke-Api "/activity/recent?limit=500"

$byFilename = @{}
if ($registry -and $registry.items) {
    foreach ($item in $registry.items) {
        if ($item.file_name) { $byFilename[$item.file_name] = $item }
    }
}

$sinceStr2   = $DropStart.AddSeconds(-2).ToString("O")
$dupSkips    = @()
$fails       = @()
if ($actAll) {
    $dupSkips = @($actAll | Where-Object { $_.action_type -eq "DuplicateSkipped" -and $_.occurred_at -gt $sinceStr2 })
    $fails    = @($actAll | Where-Object { $_.action_type -eq "MediaFailed"      -and $_.occurred_at -gt $sinceStr2 })
}

# -- 11. Post-ingestion report -----------------------------------------------
Write-S "POST-INGESTION: RESULTS"

$rhdr = " {0,-4} {1,-30} {2,-22} {3,-12} {4,-7} {5}" -f "#", "Title", "Author", "Status", "Conf", "Notes"
$rdiv = " {0,-4} {1,-30} {2,-22} {3,-12} {4,-7} {5}" -f "----", "------------------------------", "----------------------", "------------", "-------", "-----------------------"
Write-RL $rhdr; Write-RL $rdiv

$stats = @{ Staged=0; Review=0; Duplicate=0; Failed=0; Unknown=0; TotalConf=0.0; ConfCount=0 }

foreach ($f in $droppedFiles) {
    $item   = $byFilename[$f.Filename]
    $status = "?"
    $conf   = "  ?"
    $notes  = ""
    $color  = "Gray"

    if ($item) {
        $pct  = [int]($item.confidence * 100)
        $conf = "${pct}%"

        $rv = if ($item.review_item_id) { $item.review_item_id } else { $null }
        if ($rv) {
            $trigger = if ($item.review_trigger) { $item.review_trigger } else { "review" }
            $status = "Review"
            $notes  = $trigger
            $color  = "Yellow"
            $stats.Review++
        } elseif ($item.status) {
            $status = $item.status
            $notes  = if ($item.author) { "by $($item.author)" } else { "" }
            $color  = if ($pct -ge 70) { "Green" } else { "Cyan" }
            $stats.Staged++
        }
        $stats.TotalConf += $item.confidence
        $stats.ConfCount++

    } elseif ($f.Scenario -eq "duplicate") {
        $status = "Duplicate"
        $conf   = "  -"
        $notes  = "hash match -> skip"
        $color  = "DarkCyan"
        $stats.Duplicate++

    } elseif ($f.Scenario -eq "corrupt") {
        $status = "Failed"
        $conf   = "  -"
        $notes  = "corrupt file"
        $color  = "Red"
        $stats.Failed++

    } else {
        $status = "Not found"
        $conf   = "  ?"
        $notes  = "not in registry"
        $color  = "DarkYellow"
        $stats.Unknown++
    }

    $td = if ($f.Book.Title.Length  -gt 30) { $f.Book.Title.Substring(0,27)  + "..." } else { $f.Book.Title  }
    $ad = if ($f.Book.Author.Length -gt 22) { $f.Book.Author.Substring(0,19) + "..." } else { $f.Book.Author }
    $row = " {0,-4} {1,-30} {2,-22} {3,-12} {4,-7} {5}" -f $f.Num, $td, $ad, $status, $conf, $notes
    Write-R $row -c $color
}

# -- 12. Summary -------------------------------------------------------------
Write-S "SUMMARY"

$avgConf = if ($stats.ConfCount -gt 0) { [int]($stats.TotalConf / $stats.ConfCount * 100) } else { 0 }
$total   = $droppedFiles.Count
$RunEnd  = Get-Date
$RunDur  = $RunEnd - $RunStart

$summaryData = @(
    @{ L=" Total files dropped";    V=$total;                    C="White" }
    @{ L=" [OK]  Staged";          V="$($stats.Staged) ($(if($total -gt 0){[int]($stats.Staged/$total*100)}else{0})%)";    C="Green" }
    @{ L=" [!]   Sent to review";  V="$($stats.Review) ($(if($total -gt 0){[int]($stats.Review/$total*100)}else{0})%)";    C="Yellow" }
    @{ L=" [DUP] Duplicates";      V="$($stats.Duplicate) ($(if($total -gt 0){[int]($stats.Duplicate/$total*100)}else{0})%)"; C="DarkCyan" }
    @{ L=" [ERR] Failed/corrupt";  V="$($stats.Failed) ($(if($total -gt 0){[int]($stats.Failed/$total*100)}else{0})%)";    C="Red" }
    @{ L=" [?]   Unresolved";      V=$stats.Unknown;             C="DarkYellow" }
    @{ L="";                       V="";                         C="White" }
    @{ L=" Avg confidence score";  V="${avgConf}%";              C="White" }
    @{ L=" Ingestion duration";    V="$([int]$IngDuration.TotalSeconds)s"; C="White" }
    @{ L=" Total run time";        V="$([int]$RunDur.TotalSeconds)s";      C="White" }
    @{ L=" Random seed used";      V=$Seed;                      C="White" }
)

foreach ($row in $summaryData) {
    if ($row.L) {
        $line = " {0,-26} : {1}" -f $row.L, $row.V
        Write-R $line -c $row.C
    } else {
        Write-RL ""
    }
}

# -- 13. Review queue detail -------------------------------------------------
if ($stats.Review -gt 0 -and $reviewItems) {
    $rvList = @($reviewItems | Select-Object -First 20)
    if ($rvList.Count -gt 0) {
        Write-S "REVIEW QUEUE ITEMS"
        foreach ($rv in $rvList) {
            $trigger = if ($rv.review_trigger) { $rv.review_trigger } else { "unknown" }
            $conf2   = if ($rv.confidence)     { "$([int]($rv.confidence * 100))%" } else { "?" }
            $eid     = if ($rv.entity_id)      { $rv.entity_id.ToString().Substring(0,8) } else { "?" }
            Write-R "  [$eid...]  trigger=$trigger  conf=$conf2  $($rv.detail)" -c "Yellow"
        }
    }
}

# -- 14. Save report ---------------------------------------------------------
$ReportLines.Add("")
$ReportLines.Add(" Report saved : $ReportPath")
$ReportLines.Add(" Run ended    : $($RunEnd.ToString('yyyy-MM-dd HH:mm:ss'))")

[System.IO.File]::WriteAllLines($ReportPath, $ReportLines, [System.Text.Encoding]::UTF8)

Write-RL ""
Write-R " Report saved to: $ReportPath" -c "DarkGray"
Write-RL ""

# -- 15. Cleanup -------------------------------------------------------------
Remove-Item -Path $TempDir -Recurse -Force -ErrorAction SilentlyContinue
