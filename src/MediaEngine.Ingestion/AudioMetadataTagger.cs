using Microsoft.Extensions.Logging;
using MediaEngine.Ingestion.Contracts;

namespace MediaEngine.Ingestion;

/// <summary>
/// Writes metadata back into audio files (MP3, M4B, M4A, FLAC, OGG) using TagLibSharp.
/// Handles ID3v2 (MP3), MP4 atoms (M4B/M4A), Vorbis comments (FLAC/OGG).
///
/// Safety: backup-before-modify pattern — same as <see cref="EpubMetadataTagger"/>.
/// </summary>
public sealed class AudioMetadataTagger : IMetadataTagger
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
        ".mp3", ".m4b", ".m4a", ".flac", ".ogg", ".opus", ".wma",
    };

    /// <summary>
    /// Identifier claim keys written as custom tag fields. For ID3v2 these become
    /// <c>TXXX:{KEY}</c> frames; for MP4 they become reverse-DNS
    /// <c>----:com.tuvima:{key}</c> atoms. Embedding these lets re-ingestion
    /// short-circuit the matching cascade.
    /// </summary>
    private static readonly string[] CustomIdKeys =
    [
        "isbn", "asin", "audible_id",
        "apple_books_id", "apple_music_id", "apple_music_collection_id",
        "apple_artist_id", "musicbrainz_id", "wikidata_qid",
    ];

    private static void WriteCustomId(TagLib.File file, string key, string value)
    {
        if (string.IsNullOrEmpty(value)) return;

        if (file.GetTag(TagLib.TagTypes.Id3v2, false) is TagLib.Id3v2.Tag id3v2)
        {
            var frame = TagLib.Id3v2.UserTextInformationFrame.Get(id3v2, key.ToUpperInvariant(), true);
            frame.Text = [value];
            return;
        }

        if (file.GetTag(TagLib.TagTypes.Apple, false) is TagLib.Mpeg4.AppleTag appleTag)
        {
            appleTag.SetDashBox("com.tuvima", key, value);
            return;
        }

        if (file.GetTag(TagLib.TagTypes.Xiph, false) is TagLib.Ogg.XiphComment xiph)
        {
            xiph.SetField("TUVIMA:" + key.ToUpperInvariant(), value);
        }
    }

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

        var backupPath = filePath + ".tuvima.bak";
        try
        {
            File.Copy(filePath, backupPath, overwrite: true);

            using var file = TagLib.File.Create(filePath);

            if (tags.TryGetValue("title", out var title))
                file.Tag.Title = title;

            if (tags.TryGetValue("author", out var author))
                file.Tag.Performers = [author];

            if (tags.TryGetValue("artist", out var artist))
                file.Tag.Performers = [artist];

            if (tags.TryGetValue("album", out var albumName))
                file.Tag.Album = albumName;

            if (tags.TryGetValue("track_number", out var trackStr) && uint.TryParse(trackStr, out var trackNo))
                file.Tag.Track = trackNo;

            if (tags.TryGetValue("narrator", out var narrator))
            {
                // Write narrator to TXXX:NARRATOR — the same custom frame that
                // AudioProcessor reads as its primary narrator source.
                if (file.TagTypes.HasFlag(TagLib.TagTypes.Id3v2) &&
                    file.GetTag(TagLib.TagTypes.Id3v2) is TagLib.Id3v2.Tag id3v2)
                {
                    var frame = TagLib.Id3v2.UserTextInformationFrame.Get(id3v2, "NARRATOR", true);
                    frame.Text = [narrator];
                }
                else
                {
                    // Non-ID3 formats (M4A, FLAC, OGG): use Composers as fallback
                    // since AudioProcessor checks Composers for narrator on these formats.
                    file.Tag.Composers = [narrator];
                }
            }

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

            // Custom identifier fields — round-trippable on re-ingest.
            foreach (var key in CustomIdKeys)
            {
                if (tags.TryGetValue(key, out var idValue))
                    WriteCustomId(file, key, idValue);
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

        var backupPath = filePath + ".tuvima.bak";
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
