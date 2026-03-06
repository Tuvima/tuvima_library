// ──────────────────────────────────────────────────────────────────────
// GenerateTestEpubs — Creates minimal valid EPUB 3.0 files for
// end-to-end pipeline testing.
//
// Usage:
//   dotnet run --project tools/GenerateTestEpubs [output-directory]
//
// Default output: C:\Users\shaya\Downloads\books\watcher
// ──────────────────────────────────────────────────────────────────────

using System.IO.Compression;
using System.Text;

var outputDir = args.Length > 0
    ? args[0]
    : @"C:\Users\shaya\Downloads\books\watcher";

Directory.CreateDirectory(outputDir);

// ── Test books ─────────────────────────────────────────────────────────
var books = new (string FileName, string Title, string Author, string Isbn,
                 string? IsbnScheme, string Year, string Language,
                 string Publisher, string Description, string? SecondAuthor)[]
{
    ("abaddons-gate.epub",
     "Abaddon's Gate", "James S. A. Corey", "9780316129077", "ISBN",
     "2013", "en", "Orbit",
     "The third book in the Expanse series.",
     null),

    ("dune.epub",
     "Dune", "Frank Herbert", "9780441172719", null,   // bare ISBN
     "1965", "en", "Ace Books",
     "A science fiction masterpiece about the desert planet Arrakis.",
     null),

    ("the-hobbit.epub",
     "The Hobbit", "J.R.R. Tolkien", "9780547928227", null,   // bare ISBN
     "1937", "en", "Houghton Mifflin Harcourt",
     "Bilbo Baggins, a respectable hobbit, is tricked by the wizard Gandalf into joining an adventure.",
     null),

    ("neuromancer.epub",
     "Neuromancer", "William Gibson", "9780441569595", "ISBN",
     "1984", "en", "Ace Books",
     "The first novel in Gibson's Sprawl trilogy, a pioneering work of cyberpunk fiction.",
     null),

    ("good-omens.epub",
     "Good Omens", "Terry Pratchett", "9780060853983", "ISBN",
     "1990", "en", "Workman Publishing",
     "The Nice and Accurate Prophecies of Agnes Nutter, Witch.",
     "Neil Gaiman"),
};

Console.WriteLine($"Generating {books.Length} test EPUBs in: {outputDir}");
Console.WriteLine();

foreach (var book in books)
{
    var path = Path.Combine(outputDir, book.FileName);
    CreateEpub(path, book.Title, book.Author, book.Isbn, book.IsbnScheme,
               book.Year, book.Language, book.Publisher, book.Description,
               book.SecondAuthor);
    var fi = new FileInfo(path);
    Console.WriteLine($"  [{fi.Length,6:N0} B]  {book.FileName}");
    Console.WriteLine($"           Title: {book.Title}");
    Console.WriteLine($"          Author: {book.Author}{(book.SecondAuthor is not null ? $" & {book.SecondAuthor}" : "")}");
    Console.WriteLine($"            ISBN: {book.Isbn} ({(book.IsbnScheme is null ? "bare — no scheme" : $"scheme=\"{book.IsbnScheme}\"")})");
    Console.WriteLine($"            Year: {book.Year}");
    Console.WriteLine();
}

Console.WriteLine("Done. Start or restart the Engine to process these files.");

// ── EPUB creation ──────────────────────────────────────────────────────

static void CreateEpub(
    string outputPath, string title, string author, string isbn,
    string? isbnScheme, string year, string language, string publisher,
    string description, string? secondAuthor)
{
    if (File.Exists(outputPath)) File.Delete(outputPath);

    using var fs = new FileStream(outputPath, FileMode.Create);
    using var zip = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false);

    // 1. mimetype (MUST be first entry, uncompressed)
    var mimeEntry = zip.CreateEntry("mimetype", CompressionLevel.NoCompression);
    using (var w = new StreamWriter(mimeEntry.Open(), Encoding.ASCII))
        w.Write("application/epub+zip");

    // 2. META-INF/container.xml
    AddText(zip, "META-INF/container.xml", """
        <?xml version="1.0" encoding="UTF-8"?>
        <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
          <rootfiles>
            <rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/>
          </rootfiles>
        </container>
        """);

    // 3. OEBPS/content.opf
    var identifierXml = isbnScheme is not null
        ? $"""<dc:identifier id="isbn" opf:scheme="{isbnScheme}">{isbn}</dc:identifier>"""
        : $"""<dc:identifier id="isbn">{isbn}</dc:identifier>""";

    var authorXml = $"""<dc:creator opf:role="aut">{Esc(author)}</dc:creator>""";
    if (secondAuthor is not null)
        authorXml += $"\n        <dc:creator opf:role=\"aut\">{Esc(secondAuthor)}</dc:creator>";

    AddText(zip, "OEBPS/content.opf", $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <package xmlns="http://www.idpf.org/2007/opf"
                 xmlns:dc="http://purl.org/dc/elements/1.1/"
                 xmlns:opf="http://www.idpf.org/2007/opf"
                 unique-identifier="isbn" version="3.0">
          <metadata>
            <dc:title>{Esc(title)}</dc:title>
            {authorXml}
            {identifierXml}
            <dc:language>{language}</dc:language>
            <dc:publisher>{Esc(publisher)}</dc:publisher>
            <dc:description>{Esc(description)}</dc:description>
            <dc:date>{year}</dc:date>
            <meta property="dcterms:modified">2025-01-01T00:00:00Z</meta>
          </metadata>
          <manifest>
            <item id="chapter1" href="chapter1.xhtml" media-type="application/xhtml+xml"/>
            <item id="nav" href="nav.xhtml" media-type="application/xhtml+xml" properties="nav"/>
            <item id="toc" href="toc.ncx" media-type="application/x-dtbncx+xml"/>
          </manifest>
          <spine toc="toc">
            <itemref idref="chapter1"/>
          </spine>
        </package>
        """);

    // 4. OEBPS/toc.ncx
    AddText(zip, "OEBPS/toc.ncx", $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <ncx xmlns="http://www.daisy.org/z3986/2005/ncx/" version="2005-1">
          <head>
            <meta name="dtb:uid" content="{isbn}"/>
          </head>
          <docTitle><text>{Esc(title)}</text></docTitle>
          <navMap>
            <navPoint id="np1" playOrder="1">
              <navLabel><text>Chapter 1</text></navLabel>
              <content src="chapter1.xhtml"/>
            </navPoint>
          </navMap>
        </ncx>
        """);

    // 5. OEBPS/nav.xhtml (EPUB 3 navigation document — required by VersOne)
    AddText(zip, "OEBPS/nav.xhtml", $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops">
          <head><title>Navigation</title></head>
          <body>
            <nav epub:type="toc" id="toc">
              <h1>Table of Contents</h1>
              <ol>
                <li><a href="chapter1.xhtml">Chapter 1</a></li>
              </ol>
            </nav>
          </body>
        </html>
        """);

    // 6. OEBPS/chapter1.xhtml
    var authorLine = secondAuthor is not null
        ? $"{Esc(author)} &amp; {Esc(secondAuthor)}"
        : Esc(author);
    AddText(zip, "OEBPS/chapter1.xhtml", $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <html xmlns="http://www.w3.org/1999/xhtml">
          <head><title>{Esc(title)}</title></head>
          <body>
            <h1>{Esc(title)}</h1>
            <p>This is a minimal test EPUB for pipeline validation.</p>
            <p>Author: {authorLine}</p>
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

static string Esc(string s) =>
    s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
     .Replace("\"", "&quot;").Replace("'", "&apos;");
