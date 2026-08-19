using System.Security.Cryptography;
using MediaEngine.Contracts.Photos;
using MediaEngine.Domain.Configuration;
using MediaEngine.Domain.Contracts;
using MediaEngine.Storage;
using SkiaSharp;

namespace MediaEngine.Api.Services.Photos;

/// <summary>
/// Indexes photo libraries without entering the catalogue identity pipeline.
/// Files remain in place and are deduplicated by SHA-256 content identity.
/// </summary>
public sealed class PhotoLibraryService(
    PhotoLibraryRepository repository,
    IConfigurationLoader configuration,
    ILogger<PhotoLibraryService> logger)
{
    private static readonly IReadOnlyDictionary<string, string> ImageTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".png"] = "image/png",
            [".webp"] = "image/webp",
            [".gif"] = "image/gif",
            [".bmp"] = "image/bmp",
            [".tif"] = "image/tiff",
            [".tiff"] = "image/tiff",
            [".heic"] = "image/heic",
            [".heif"] = "image/heif",
            [".avif"] = "image/avif",
        };

    public async Task<PhotoScanResultDto> ScanAsync(CancellationToken ct = default)
    {
        var filesSeen = 0;
        var photosAdded = 0;
        var sourcesAdded = 0;
        var duplicates = 0;
        var errors = 0;

        foreach (var library in configuration.LoadLibraries().Libraries.Where(library =>
                     string.Equals(library.Kind, LibraryKinds.Photos, StringComparison.OrdinalIgnoreCase)))
        {
            if (!Guid.TryParse(library.Id, out var libraryId))
            {
                continue;
            }

            foreach (var root in library.SourcePaths.Where(Directory.Exists))
            {
                var option = library.IncludeSubdirectories
                    ? SearchOption.AllDirectories
                    : SearchOption.TopDirectoryOnly;
                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(root, "*", option);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    logger.LogWarning(ex, "Could not enumerate photo library {Root}", root);
                    errors++;
                    continue;
                }

                foreach (var path in files)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!ImageTypes.TryGetValue(Path.GetExtension(path), out var mimeType))
                    {
                        continue;
                    }

                    filesSeen++;
                    try
                    {
                        var info = new FileInfo(path);
                        var hash = await HashAsync(path, ct);
                        var (width, height) = ReadDimensions(path);
                        var metadata = ReadMetadata(path, info);
                        var result = await repository.UpsertAsync(
                            libraryId,
                            Path.GetFullPath(path),
                            hash,
                            info.Name,
                            metadata.CapturedAt,
                            width,
                            height,
                            mimeType,
                            info.Length,
                            info.LastWriteTimeUtc,
                            metadata.Latitude,
                            metadata.Longitude,
                            metadata.CameraMake,
                            metadata.CameraModel,
                            ct);
                        if (result.PhotoAdded)
                        {
                            photosAdded++;
                        }

                        if (result.SourceAdded)
                        {
                            sourcesAdded++;
                        }

                        if (!result.PhotoAdded && result.SourceAdded)
                        {
                            duplicates++;
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
                    {
                        logger.LogWarning(ex, "Could not index photo {Path}", path);
                        errors++;
                    }
                }
            }
        }

        return new PhotoScanResultDto(filesSeen, photosAdded, sourcesAdded, duplicates, errors);
    }

    private static async Task<string> HashAsync(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexStringLower(hash);
    }

    private static (int? Width, int? Height) ReadDimensions(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var codec = SKCodec.Create(stream);
            return codec is null ? (null, null) : (codec.Info.Width, codec.Info.Height);
        }
        catch
        {
            return (null, null);
        }
    }

    private static PhotoMetadata ReadMetadata(string path, FileInfo info)
    {
        try
        {
            using var file = TagLib.File.Create(path);
            if (file is TagLib.Image.File image)
            {
                var tag = image.ImageTag;
                var date = tag.DateTime;
                var capturedAt = date.HasValue
                    ? new DateTimeOffset(DateTime.SpecifyKind(date.Value, DateTimeKind.Local)).ToUniversalTime()
                    : ResolveFileDate(info);
                return new PhotoMetadata(
                    capturedAt,
                    ValidCoordinate(tag.Latitude, -90, 90),
                    ValidCoordinate(tag.Longitude, -180, 180),
                    tag.Make,
                    tag.Model);
            }
        }
        catch
        {
            // Unsupported or malformed EXIF falls back to filesystem time.
        }

        return new PhotoMetadata(ResolveFileDate(info), null, null, null, null);
    }

    private static DateTimeOffset ResolveFileDate(FileInfo info)
    {
        var created = new DateTimeOffset(info.CreationTimeUtc, TimeSpan.Zero);
        var modified = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero);
        return created.Year >= 1990 && created <= DateTimeOffset.UtcNow.AddDays(1)
            ? created
            : modified;
    }

    private static double? ValidCoordinate(double? value, double minimum, double maximum) =>
        value.HasValue && value.Value >= minimum && value.Value <= maximum ? value : null;

    private sealed record PhotoMetadata(
        DateTimeOffset CapturedAt,
        double? Latitude,
        double? Longitude,
        string? CameraMake,
        string? CameraModel);
}
