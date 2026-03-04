using Microsoft.Extensions.Logging;
using Tanaste.Ingestion.Contracts;

namespace Tanaste.Ingestion;

/// <summary>
/// Writes metadata back into audio files (MP3, M4B, M4A, FLAC, OGG) using TagLibSharp.
/// Handles ID3v2 (MP3), MP4 atoms (M4B/M4A), Vorbis comments (FLAC/OGG).
///
/// Safety: backup-before-modify pattern — same as <see cref="EpubMetadataTagger"/>.
/// </summary>
public sealed class AudioMetadataTagger : IMetadataTagger
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".m4b", ".m4a", ".flac", ".ogg", ".opus", ".wma",
    };

    private readonly ILogger<AudioMetadataTagger> _logger;

    public AudioMetadataTagger(ILogger<AudioMetadataTagger> logger)
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
    public async Task WriteTagsAsync(
        string filePath,
        IReadOnlyDictionary<string, string> tags,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (!File.Exists(filePath))
        {
            _logger.LogWarning("AudioTagger: file not found — {Path}", filePath);
            return;
        }

        var backupPath = filePath + ".tanaste.bak";
        try
        {
            File.Copy(filePath, backupPath, overwrite: true);

            using var file = TagLib.File.Create(filePath);

            if (tags.TryGetValue("title", out var title))
                file.Tag.Title = title;

            if (tags.TryGetValue("author", out var author))
                file.Tag.Performers = [author];

            if (tags.TryGetValue("narrator", out var narrator))
                file.Tag.Performers = [narrator];

            if (tags.TryGetValue("series", out var series))
                file.Tag.Album = series;

            if (tags.TryGetValue("series_position", out var pos) && uint.TryParse(pos, out var trackNum))
                file.Tag.Track = trackNum;

            if (tags.TryGetValue("genre", out var genre))
                file.Tag.Genres = [genre];

            if (tags.TryGetValue("description", out var desc))
                file.Tag.Comment = desc;

            if (tags.TryGetValue("year", out var yearStr) && uint.TryParse(yearStr, out var year))
                file.Tag.Year = year;

            if (tags.TryGetValue("publisher", out var publisher))
            {
                // TagLib doesn't have a dedicated publisher property;
                // store in the first available custom field.
                file.Tag.Publisher = publisher;
            }

            file.Save();

            // Backup cleanup — success.
            if (File.Exists(backupPath))
                File.Delete(backupPath);

            _logger.LogInformation("AudioTagger: wrote {Count} tags to {Path}",
                tags.Count, filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AudioTagger: failed to write tags to {Path} — restoring backup", filePath);
            RestoreBackup(filePath, backupPath);
            throw;
        }

        await Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task WriteCoverArtAsync(
        string filePath,
        byte[] imageData,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (!File.Exists(filePath) || imageData.Length == 0)
            return;

        var backupPath = filePath + ".tanaste.bak";
        try
        {
            File.Copy(filePath, backupPath, overwrite: true);

            using var file = TagLib.File.Create(filePath);
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

            if (File.Exists(backupPath))
                File.Delete(backupPath);

            _logger.LogInformation("AudioTagger: wrote cover art ({Size} bytes) to {Path}",
                imageData.Length, filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AudioTagger: failed to write cover art to {Path} — restoring backup", filePath);
            RestoreBackup(filePath, backupPath);
            throw;
        }

        await Task.CompletedTask;
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
                "AudioTagger: CRITICAL — backup restore also failed for {Path}", filePath);
        }
    }
}
