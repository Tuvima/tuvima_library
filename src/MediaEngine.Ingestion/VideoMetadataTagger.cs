using Microsoft.Extensions.Logging;
using MediaEngine.Ingestion.Contracts;

namespace MediaEngine.Ingestion;

/// <summary>
/// Writes metadata back into video files (MKV, MP4, AVI, WebM) using TagLibSharp.
/// Handles Matroska tags (MKV) and MP4 atoms.
///
/// Safety: backup-before-modify pattern — same as <see cref="EpubMetadataTagger"/>.
/// </summary>
public sealed class VideoMetadataTagger : IMetadataTagger
{
    /// <summary>
    /// Bumped manually whenever this tagger gains a new write or changes the
    /// way an existing field is written. Combined with the per-media-type
    /// JSON slice from <c>writeback-fields.json</c> to compute the writeback
    /// hash that the auto re-tag sweep uses to detect stale files.
    /// </summary>
    public const int Version = 1;

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".avi", ".webm", ".mov",
    };

    /// <summary>
    /// Identifier claim keys written as iTunes reverse-DNS atoms
    /// (<c>----:com.tuvima:{key}</c>). Embedding these in the file lets
    /// re-ingestion short-circuit the matching cascade.
    /// </summary>
    private static readonly string[] CustomIdKeys =
    [
        "imdb_id", "tmdb_id", "tvdb_id", "apple_itunes_id",
        "wikidata_qid", "show_wikidata_qid",
    ];

    private static void SetAppleText(TagLib.Mpeg4.AppleTag appleTag, string fourCc, string value)
    {
        if (string.IsNullOrEmpty(value)) return;
        var box = TagLib.ByteVector.FromString(fourCc, TagLib.StringType.Latin1);
        appleTag.SetText(box, value);
    }

    private readonly ILogger<VideoMetadataTagger> _logger;

    public VideoMetadataTagger(ILogger<VideoMetadataTagger> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public bool CanHandle(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return false;
        return SupportedExtensions.Contains(Path.GetExtension(filePath));
    }

    /// <inheritdoc/>
    public Task WriteTagsAsync(
        string filePath,
        IReadOnlyDictionary<string, string> tags,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (!File.Exists(filePath))
        {
            _logger.LogWarning("VideoTagger: file not found — {Path}", filePath);
            return Task.CompletedTask;
        }

        var backupPath = filePath + ".tuvima.bak";
        try
        {
            // For large video files, we skip backup to avoid doubling disk usage.
            // TagLibSharp modifies in-place; risk is low for metadata-only writes.
            var fileSize = new FileInfo(filePath).Length;
            var shouldBackup = fileSize < 500 * 1024 * 1024; // 500 MB threshold

            if (shouldBackup)
                File.Copy(filePath, backupPath, overwrite: true);

            using var file = CreateTagFileOrSkip(filePath);
            if (file is null)
            {
                if (shouldBackup && File.Exists(backupPath))
                    File.Delete(backupPath);

                return Task.CompletedTask;
            }

            if (tags.TryGetValue("title", out var title))
                file.Tag.Title = title;

            if (tags.TryGetValue("director", out var director))
                file.Tag.Performers = [director];

            if (tags.TryGetValue("author", out var author) && file.Tag.Performers.Length == 0)
                file.Tag.Performers = [author];

            if (tags.TryGetValue("genre", out var genre))
                file.Tag.Genres = [genre];

            if (tags.TryGetValue("description", out var desc))
                file.Tag.Comment = desc;

            if (tags.TryGetValue("year", out var yearStr) && uint.TryParse(yearStr, out var year))
                file.Tag.Year = year;

            // MP4-specific TV atoms and custom identifiers via the iTunes AppleTag.
            // Matroska files only get the standard Tag fields above; rich custom
            // tagging on MKV is deferred until we add a SimpleTag writer.
            var appleTag = file.GetTag(TagLib.TagTypes.Apple, false) as TagLib.Mpeg4.AppleTag;
            if (appleTag is not null)
            {
                if (tags.TryGetValue("show_name", out var showName))
                    SetAppleText(appleTag, "tvsh", showName);
                if (tags.TryGetValue("episode_title", out var episodeTitle))
                    SetAppleText(appleTag, "tven", episodeTitle);
                if (tags.TryGetValue("network", out var network))
                    SetAppleText(appleTag, "tvnn", network);

                // Custom identifier atoms (reverse-DNS) — round-trippable on re-ingest.
                foreach (var key in CustomIdKeys)
                {
                    if (tags.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                        appleTag.SetDashBox("com.tuvima", key, value);
                }
            }

            try
            {
                file.Save();
            }
            catch (ArgumentException argEx) when (IsNanDurationMetadata(argEx))
            {
                _logger.LogWarning("VideoTagger: skipping save for {Path} — file contains NaN duration metadata", filePath);
                return Task.CompletedTask;
            }

            if (shouldBackup && File.Exists(backupPath))
                File.Delete(backupPath);

            _logger.LogInformation("VideoTagger: wrote {Count} tags to {Path}",
                tags.Count, filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "VideoTagger: failed to write tags to {Path} — restoring backup", filePath);
            RestoreBackup(filePath, backupPath);
            throw;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task WriteCoverArtAsync(
        string filePath,
        byte[] imageData,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (!File.Exists(filePath) || imageData.Length == 0)
            return Task.CompletedTask;

        try
        {
            using var file = CreateTagFileOrSkip(filePath);
            if (file is null)
                return Task.CompletedTask;

            file.Tag.Pictures =
            [
                new TagLib.Picture(new TagLib.ByteVector(imageData))
                {
                    Type        = TagLib.PictureType.FrontCover,
                    MimeType    = "image/jpeg",
                    Description = "Cover",
                },
            ];
            file.Save();

            _logger.LogInformation("VideoTagger: wrote cover art ({Size} bytes) to {Path}",
                imageData.Length, filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "VideoTagger: failed to write cover art to {Path}", filePath);
        }

        return Task.CompletedTask;
    }

    private TagLib.File? CreateTagFileOrSkip(string filePath)
    {
        try
        {
            return TagLib.File.Create(filePath);
        }
        catch (ArgumentException argEx) when (IsNanDurationMetadata(argEx))
        {
            _logger.LogWarning("VideoTagger: skipping {Path} — file contains NaN duration metadata", filePath);
            return null;
        }
    }

    private static bool IsNanDurationMetadata(ArgumentException ex)
        => ex.Message.Contains("Not-a-Number", StringComparison.OrdinalIgnoreCase)
           || ex.Message.Contains("NaN", StringComparison.OrdinalIgnoreCase);

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
                "VideoTagger: CRITICAL — backup restore also failed for {Path}", filePath);
        }
    }
}
