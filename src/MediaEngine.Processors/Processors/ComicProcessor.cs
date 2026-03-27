using System.IO.Compression;
using System.Xml.Linq;
using MediaEngine.Domain.Enums;
using MediaEngine.Processors.Contracts;
using MediaEngine.Processors.Models;

namespace MediaEngine.Processors.Processors;

/// <summary>
/// Identifies and extracts metadata from comic book archive formats.
///
/// ──────────────────────────────────────────────────────────────────
/// Format detection (spec: Phase 5 – Magic-Byte Detection)
/// ──────────────────────────────────────────────────────────────────
///  • CBZ  (Comic Book ZIP):  ZIP magic 50 4B 03 04  +  ≥1 image entry
///  • CBR  (Comic Book RAR):  RAR magic 52 61 72 21 1A 07  (detection only;
///    page counting requires a RAR library and returns null confidence)
///
///  A ZIP file that contains no image entries (e.g. an EPUB with no images) is
///  not claimed by this processor.  EPUBs are explicitly excluded via the
///  absence of a "mimetype" entry equal to "application/epub+zip", which the
///  <see cref="EpubProcessor"/> would have already claimed at higher priority.
///
/// ──────────────────────────────────────────────────────────────────
/// Page counting (spec: Phase 5 – Segmentation)
/// ──────────────────────────────────────────────────────────────────
///  For CBZ files: page_count = number of ZIP entries whose name ends in
///  a recognised image extension (.jpg, .jpeg, .png, .gif, .webp, .avif).
///  Hidden entries and directory entries (entries with trailing '/') are skipped.
///
///  For CBR files: page_count is not emitted (no BCL RAR support).
///
/// ──────────────────────────────────────────────────────────────────
/// Extracted claims
/// ──────────────────────────────────────────────────────────────────
///  • title          (confidence 0.5 — filename stem; 0.8 from ComicInfo.xml)
///  • container      (confidence 1.0 — "CBZ" or "CBR" from magic bytes)
///  • page_count     (confidence 1.0 — CBZ image count; 0.9 from ComicInfo.xml)
///  • author         (confidence 0.8 — ComicInfo.xml Writer)
///  • illustrator    (confidence 0.8 — ComicInfo.xml Penciller)
///  • genre          (confidence 0.7 — ComicInfo.xml Genre)
///  • description    (confidence 0.7 — ComicInfo.xml Summary)
///  • year           (confidence 0.8 — ComicInfo.xml Year)
///  • publisher      (confidence 0.7 — ComicInfo.xml Publisher)
///  • series         (confidence 0.8 — ComicInfo.xml Series)
///  • series_position(confidence 0.8 — ComicInfo.xml Number)
///
/// Spec: Phase 5 – Media Processor Architecture § ComicProcessor.
/// </summary>
public sealed class ComicProcessor : IMediaProcessor
{
    // ZIP local-file-header magic.
    private static ReadOnlySpan<byte> ZipMagic => [0x50, 0x4B, 0x03, 0x04];

    // RAR 1.5+ magic: "Rar!\x1A\x07\x00"
    private static ReadOnlySpan<byte> RarMagic => [0x52, 0x61, 0x72, 0x21, 0x1A, 0x07];

    private static readonly HashSet<string> ImageExtensions =
        new([".jpg", ".jpeg", ".png", ".gif", ".webp", ".avif"],
            StringComparer.OrdinalIgnoreCase);

    // EPUB mimetype entry name and value — used to exclude EPUBs.
    private const string EpubMimeEntry  = "mimetype";
    private const string EpubMimeValue  = "application/epub+zip";

    private enum ComicContainer { Unknown, Cbz, Cbr }

    /// <inheritdoc/>
    public MediaType SupportedType => MediaType.Comics;

    /// <inheritdoc/>
    /// <remarks>
    /// Priority 85 — below EpubProcessor (100), which claims ZIP-based EPUBs
    /// before this processor has a chance to inspect them.
    /// </remarks>
    public int Priority => 85;

    // -------------------------------------------------------------------------
    // IMediaProcessor
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public bool CanProcess(string filePath)
    {
        if (!File.Exists(filePath)) return false;
        return DetectContainer(filePath) != ComicContainer.Unknown;
    }

    /// <inheritdoc/>
    public Task<ProcessorResult> ProcessAsync(string filePath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ct.ThrowIfCancellationRequested();

        var container = DetectContainer(filePath);
        if (container == ComicContainer.Unknown)
            return Task.FromResult(Corrupt(filePath, "No recognised comic archive magic bytes."));

        var claims = new List<ExtractedClaim>();

        // Title from filename stem.
        var stem = Path.GetFileNameWithoutExtension(filePath);
        if (!string.IsNullOrWhiteSpace(stem))
            claims.Add(Claim("title", stem, 0.5));

        // Container label — authoritative.
        claims.Add(Claim("container", container == ComicContainer.Cbz ? "CBZ" : "CBR", 1.0));

        // Page count — CBZ only.
        if (container == ComicContainer.Cbz)
        {
            int? pageCount = CountPages(filePath);
            if (pageCount.HasValue)
                claims.Add(Claim("page_count", pageCount.Value.ToString(), 1.0));

            // ComicInfo.xml — standard metadata embedded in CBZ archives.
            ParseComicInfoXml(filePath, claims);
        }

        return Task.FromResult(new ProcessorResult
        {
            FilePath     = filePath,
            DetectedType = MediaType.Comics,
            Claims       = claims,
        });
    }

    // -------------------------------------------------------------------------
    // Magic-byte detection
    // -------------------------------------------------------------------------

    private static ComicContainer DetectContainer(string filePath)
    {
        try
        {
            Span<byte> header = stackalloc byte[6];
            using var fs = new FileStream(
                filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 6, FileOptions.None);

            int read = fs.Read(header);
            if (read < 4) return ComicContainer.Unknown;

            // RAR: 6-byte signature
            if (read >= 6 && header[..6].SequenceEqual(RarMagic))
                return ComicContainer.Cbr;

            // ZIP: 4-byte signature
            if (header[..4].SequenceEqual(ZipMagic))
                return IsComicZip(filePath) ? ComicContainer.Cbz : ComicContainer.Unknown;

            return ComicContainer.Unknown;
        }
        catch (IOException)               { return ComicContainer.Unknown; }
        catch (UnauthorizedAccessException) { return ComicContainer.Unknown; }
    }

    /// <summary>
    /// Distinguishes CBZ from EPUB and other ZIP-based formats by checking:
    ///  1. No "mimetype" entry with EPUB content.
    ///  2. At least one image entry.
    /// </summary>
    private static bool IsComicZip(string filePath)
    {
        try
        {
            using var zip = ZipFile.OpenRead(filePath);

            // Presence of the EPUB mimetype entry disqualifies this ZIP.
            var mimeEntry = zip.GetEntry(EpubMimeEntry);
            if (mimeEntry is not null)
            {
                using var reader = new System.IO.StreamReader(mimeEntry.Open());
                if (reader.ReadToEnd().Trim()
                        .Equals(EpubMimeValue, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            // Must contain at least one image entry.
            return zip.Entries.Any(IsImageEntry);
        }
        catch (InvalidDataException) { return false; }
        catch (IOException)          { return false; }
    }

    // -------------------------------------------------------------------------
    // Page counting (CBZ)
    // -------------------------------------------------------------------------

    private static int? CountPages(string filePath)
    {
        try
        {
            using var zip = ZipFile.OpenRead(filePath);
            int count = zip.Entries.Count(IsImageEntry);
            return count > 0 ? count : null;
        }
        catch (InvalidDataException) { return null; }
        catch (IOException)          { return null; }
    }

    private static bool IsImageEntry(ZipArchiveEntry entry)
    {
        // Skip directory entries (name ends with '/').
        if (entry.FullName.EndsWith('/')) return false;
        var ext = Path.GetExtension(entry.Name);
        return ImageExtensions.Contains(ext);
    }

    // -------------------------------------------------------------------------
    // ComicInfo.xml parsing (CBZ)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Looks for a <c>ComicInfo.xml</c> entry inside the CBZ archive (case-insensitive)
    /// and extracts metadata claims from its standard elements.
    /// A malformed or missing ComicInfo.xml is silently ignored.
    /// </summary>
    private static void ParseComicInfoXml(string filePath, List<ExtractedClaim> claims)
    {
        try
        {
            using var zip = ZipFile.OpenRead(filePath);

            var comicInfoEntry = zip.Entries
                .FirstOrDefault(e => e.Name.Equals("ComicInfo.xml", StringComparison.OrdinalIgnoreCase));

            if (comicInfoEntry is null) return;

            using var stream = comicInfoEntry.Open();
            var doc = XDocument.Load(stream);
            var root = doc.Root;
            if (root is null) return;

            AddClaimIfPresent(root, "Title",     "title",           0.8, claims);
            AddClaimIfPresent(root, "Writer",    "author",          0.8, claims);
            AddClaimIfPresent(root, "Penciller", "illustrator",     0.8, claims);
            AddClaimIfPresent(root, "Genre",     "genre",           0.7, claims);
            AddClaimIfPresent(root, "Summary",   "description",     0.7, claims);
            AddClaimIfPresent(root, "Year",      "year",            0.8, claims);
            AddClaimIfPresent(root, "Publisher", "publisher",       0.7, claims);
            AddClaimIfPresent(root, "Series",    "series",          0.8, claims);
            AddClaimIfPresent(root, "Number",    "series_position", 0.8, claims);
            AddClaimIfPresent(root, "PageCount", "page_count",      0.9, claims);
        }
        catch (InvalidDataException) { /* corrupt ZIP — already handled by earlier steps */ }
        catch (IOException)          { /* file access issue */ }
        catch (System.Xml.XmlException) { /* malformed XML — skip silently */ }
    }

    /// <summary>
    /// Reads a single XML element value and adds it as a claim if non-empty.
    /// </summary>
    private static void AddClaimIfPresent(
        XElement root, string elementName, string claimKey, double confidence,
        List<ExtractedClaim> claims)
    {
        var value = root.Element(elementName)?.Value;
        if (!string.IsNullOrWhiteSpace(value))
            claims.Add(Claim(claimKey, value.Trim(), confidence));
    }

    // -------------------------------------------------------------------------
    // Factory helpers
    // -------------------------------------------------------------------------

    private static ExtractedClaim Claim(string key, string value, double confidence) => new()
    {
        Key        = key,
        Value      = value,
        Confidence = confidence,
    };

    private static ProcessorResult Corrupt(string filePath, string reason) => new()
    {
        FilePath      = filePath,
        DetectedType  = MediaType.Comics,
        IsCorrupt     = true,
        CorruptReason = reason,
    };
}
