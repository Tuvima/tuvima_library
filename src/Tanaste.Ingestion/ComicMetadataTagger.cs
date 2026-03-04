using System.IO.Compression;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Tanaste.Ingestion.Contracts;

namespace Tanaste.Ingestion;

/// <summary>
/// Writes metadata back into CBZ comic archives by creating or updating a
/// <c>ComicInfo.xml</c> file inside the ZIP container.
///
/// CBR (RAR) archives are read-only — this tagger does not support them.
/// Uses <see cref="System.IO.Compression"/> (BCL) — no new dependency.
///
/// Safety: backup-before-modify pattern — same as <see cref="EpubMetadataTagger"/>.
/// </summary>
public sealed class ComicMetadataTagger : IMetadataTagger
{
    private const string ComicInfoEntry = "ComicInfo.xml";

    private readonly ILogger<ComicMetadataTagger> _logger;

    public ComicMetadataTagger(ILogger<ComicMetadataTagger> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public bool CanHandle(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return false;
        // Only CBZ (ZIP-based). CBR (RAR) is not supported for write-back.
        return Path.GetExtension(filePath).Equals(".cbz", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public async Task WriteTagsAsync(
        string filePath,
        IReadOnlyDictionary<string, string> tags,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (!File.Exists(filePath))
        {
            _logger.LogWarning("ComicTagger: file not found — {Path}", filePath);
            return;
        }

        var backupPath = filePath + ".tanaste.bak";
        try
        {
            File.Copy(filePath, backupPath, overwrite: true);

            using var zip = ZipFile.Open(filePath, ZipArchiveMode.Update);

            // Load or create ComicInfo.xml.
            var entry = zip.GetEntry(ComicInfoEntry);
            XDocument doc;
            if (entry is not null)
            {
                using var stream = entry.Open();
                doc = await XDocument.LoadAsync(stream, LoadOptions.None, ct);
            }
            else
            {
                doc = new XDocument(new XElement("ComicInfo"));
            }

            var root = doc.Root!;

            SetElement(root, "Title",   tags, "title");
            SetElement(root, "Writer",  tags, "author");
            SetElement(root, "Genre",   tags, "genre");
            SetElement(root, "Summary", tags, "description");
            SetElement(root, "Series",  tags, "series");
            SetElement(root, "Number",  tags, "series_position");

            if (tags.TryGetValue("year", out var yearStr) && int.TryParse(yearStr, out _))
                SetElementDirect(root, "Year", yearStr);

            if (tags.TryGetValue("publisher", out var pub))
                SetElementDirect(root, "Publisher", pub);

            if (tags.TryGetValue("illustrator", out var illustrator))
                SetElementDirect(root, "Penciller", illustrator);

            if (tags.TryGetValue("page_count", out var pages) && int.TryParse(pages, out _))
                SetElementDirect(root, "PageCount", pages);

            // Remove existing entry and re-add with updated content.
            entry?.Delete();
            var newEntry = zip.CreateEntry(ComicInfoEntry, CompressionLevel.Optimal);
            using (var outStream = newEntry.Open())
            {
                await doc.SaveAsync(outStream, SaveOptions.None, ct);
            }

            // Backup cleanup — success.
            if (File.Exists(backupPath))
                File.Delete(backupPath);

            _logger.LogInformation("ComicTagger: wrote ComicInfo.xml with {Count} tags to {Path}",
                tags.Count, filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ComicTagger: failed to write tags to {Path} — restoring backup", filePath);
            RestoreBackup(filePath, backupPath);
            throw;
        }
    }

    /// <inheritdoc/>
    public Task WriteCoverArtAsync(
        string filePath,
        byte[] imageData,
        CancellationToken ct = default)
    {
        // CBZ cover art is the first image file in the archive (by sort order).
        // Modifying the image order is destructive — skip cover art write-back for comics.
        _logger.LogDebug("ComicTagger: cover art write-back is not supported for CBZ archives.");
        return Task.CompletedTask;
    }

    private static void SetElement(XElement root, string elementName,
        IReadOnlyDictionary<string, string> tags, string tagKey)
    {
        if (tags.TryGetValue(tagKey, out var value) && !string.IsNullOrWhiteSpace(value))
            SetElementDirect(root, elementName, value);
    }

    private static void SetElementDirect(XElement root, string elementName, string value)
    {
        var el = root.Element(elementName);
        if (el is not null)
            el.Value = value;
        else
            root.Add(new XElement(elementName, value));
    }

    private void RestoreBackup(string filePath, string backupPath)
    {
        try
        {
            if (File.Exists(backupPath))
                File.Copy(backupPath, filePath, overwrite: true);
        }
        catch (Exception restoreEx)
        {
            _logger.LogCritical(restoreEx,
                "ComicTagger: CRITICAL — backup restore also failed for {Path}", filePath);
        }
    }
}
