using System.Security.Cryptography;
using MediaEngine.Contracts.LocalAssets;
using MediaEngine.Domain.Configuration;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.PersonalMedia;
using MediaEngine.Domain.Services;
using MediaEngine.Storage.Contracts;
using SkiaSharp;

namespace MediaEngine.Api.Services.LocalAssets;

/// <summary>
/// Indexes personal View libraries in place. This service performs local-only
/// extraction and deliberately has no dependency on catalogue identity or
/// external metadata providers.
/// </summary>
public sealed class ViewLibraryService(
    ILocalAssetRepository repository,
    IConfigurationLoader configuration,
    ILibraryAccessEvaluator accessEvaluator,
    IViewPersonalSpaceRepository spaces,
    ILogger<ViewLibraryService> logger)
{
    private const int MaxDocumentCharacters = 256 * 1024;

    private static readonly IReadOnlyDictionary<string, FileType> SupportedTypes =
        new Dictionary<string, FileType>(StringComparer.OrdinalIgnoreCase)
        {
            [".jpg"] = new(LocalAssetMediaKinds.Image, "image/jpeg"),
            [".jpeg"] = new(LocalAssetMediaKinds.Image, "image/jpeg"),
            [".png"] = new(LocalAssetMediaKinds.Image, "image/png"),
            [".webp"] = new(LocalAssetMediaKinds.Image, "image/webp"),
            [".gif"] = new(LocalAssetMediaKinds.Image, "image/gif"),
            [".bmp"] = new(LocalAssetMediaKinds.Image, "image/bmp"),
            [".tif"] = new(LocalAssetMediaKinds.Image, "image/tiff"),
            [".tiff"] = new(LocalAssetMediaKinds.Image, "image/tiff"),
            [".heic"] = new(LocalAssetMediaKinds.Image, "image/heic"),
            [".heif"] = new(LocalAssetMediaKinds.Image, "image/heif"),
            [".avif"] = new(LocalAssetMediaKinds.Image, "image/avif"),
            [".dng"] = new(LocalAssetMediaKinds.Image, "image/x-adobe-dng"),
            [".cr2"] = new(LocalAssetMediaKinds.Image, "image/x-canon-cr2"),
            [".cr3"] = new(LocalAssetMediaKinds.Image, "image/x-canon-cr3"),
            [".nef"] = new(LocalAssetMediaKinds.Image, "image/x-nikon-nef"),
            [".arw"] = new(LocalAssetMediaKinds.Image, "image/x-sony-arw"),
            [".raf"] = new(LocalAssetMediaKinds.Image, "image/x-fuji-raf"),
            [".mp4"] = new(LocalAssetMediaKinds.Video, "video/mp4"),
            [".m4v"] = new(LocalAssetMediaKinds.Video, "video/x-m4v"),
            [".mov"] = new(LocalAssetMediaKinds.Video, "video/quicktime"),
            [".avi"] = new(LocalAssetMediaKinds.Video, "video/x-msvideo"),
            [".wmv"] = new(LocalAssetMediaKinds.Video, "video/x-ms-wmv"),
            [".mkv"] = new(LocalAssetMediaKinds.Video, "video/x-matroska"),
            [".webm"] = new(LocalAssetMediaKinds.Video, "video/webm"),
            [".pdf"] = new(LocalAssetMediaKinds.Document, "application/pdf"),
            [".doc"] = new(LocalAssetMediaKinds.Document, "application/msword"),
            [".docx"] = new(LocalAssetMediaKinds.Document, "application/vnd.openxmlformats-officedocument.wordprocessingml.document"),
            [".xls"] = new(LocalAssetMediaKinds.Document, "application/vnd.ms-excel"),
            [".xlsx"] = new(LocalAssetMediaKinds.Document, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"),
            [".ppt"] = new(LocalAssetMediaKinds.Document, "application/vnd.ms-powerpoint"),
            [".pptx"] = new(LocalAssetMediaKinds.Document, "application/vnd.openxmlformats-officedocument.presentationml.presentation"),
            [".txt"] = new(LocalAssetMediaKinds.Document, "text/plain"),
            [".md"] = new(LocalAssetMediaKinds.Document, "text/markdown"),
            [".rtf"] = new(LocalAssetMediaKinds.Document, "application/rtf"),
            [".odt"] = new(LocalAssetMediaKinds.Document, "application/vnd.oasis.opendocument.text"),
            [".ods"] = new(LocalAssetMediaKinds.Document, "application/vnd.oasis.opendocument.spreadsheet"),
            [".odp"] = new(LocalAssetMediaKinds.Document, "application/vnd.oasis.opendocument.presentation"),
            [".csv"] = new(LocalAssetMediaKinds.Document, "text/csv"),
            [".json"] = new(LocalAssetMediaKinds.Document, "application/json"),
            [".xml"] = new(LocalAssetMediaKinds.Document, "application/xml"),
            [".mp3"] = new(LocalAssetMediaKinds.Audio, "audio/mpeg"),
            [".m4a"] = new(LocalAssetMediaKinds.Audio, "audio/mp4"),
            [".aac"] = new(LocalAssetMediaKinds.Audio, "audio/aac"),
            [".wav"] = new(LocalAssetMediaKinds.Audio, "audio/wav"),
            [".flac"] = new(LocalAssetMediaKinds.Audio, "audio/flac"),
            [".ogg"] = new(LocalAssetMediaKinds.Audio, "audio/ogg"),
            [".opus"] = new(LocalAssetMediaKinds.Audio, "audio/opus"),
            [".aiff"] = new(LocalAssetMediaKinds.Audio, "audio/aiff"),
            // XMP files are indexed only as companions to an adjacent RAW set.
            [".xmp"] = new(LocalAssetMediaKinds.Other, "application/rdf+xml"),
        };

    private static readonly IReadOnlySet<string> RawExtensions = new HashSet<string>(
        [".dng", ".cr2", ".cr3", ".nef", ".arw", ".raf"],
        StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlySet<string> JpegExtensions = new HashSet<string>(
        [".jpg", ".jpeg"],
        StringComparer.OrdinalIgnoreCase);

    public bool IsPersonalViewLibrary(Guid libraryId) => FindLibrary(libraryId) is not null;

    public bool CanAccess(
        Guid libraryId,
        Guid? profileId,
        string? role,
        LibraryAccessAction action)
    {
        var library = FindLibrary(libraryId);
        if (library is null || !Guid.TryParse(library.OwnerProfileId, out var ownerProfileId))
        {
            return false;
        }

        var authorizedProfileIds = library.AuthorizedProfileIds
            .Select(value => Guid.TryParse(value, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .ToHashSet();
        return accessEvaluator.IsAllowed(
            new LibraryAccessSubject(profileId ?? Guid.Empty, role ?? string.Empty),
            new LibraryAccessPolicy
            {
                OwnerProfileId = ownerProfileId,
                Visibility = library.Visibility,
                AuthorizedProfileIds = authorizedProfileIds,
            },
            action);
    }

    public IReadOnlyList<ViewLibrarySummaryDto> GetLibraries(
        Guid? profileId,
        string? role,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return GetPersonalViewLibraries()
            .Where(library => Guid.TryParse(library.Id, out var id)
                && CanAccess(id, profileId, role, LibraryAccessAction.Read))
            .Select(library =>
            {
                var id = Guid.Parse(library.Id);
                return new ViewLibrarySummaryDto(
                    id,
                    library.Name,
                    library.Presentation,
                    library.Visibility,
                    Count(id, null, ct),
                    Count(id, LocalAssetMediaKinds.Image, ct),
                    Count(id, LocalAssetMediaKinds.Video, ct),
                    Count(id, LocalAssetMediaKinds.Document, ct),
                    Count(id, LocalAssetMediaKinds.Audio, ct));
            })
            .ToList();
    }

    public async Task<LocalAssetScanResultDto?> ScanAsync(
        Guid libraryId,
        CancellationToken ct = default)
    {
        var library = FindLibrary(libraryId);
        if (library is null) return null;

        var filesSeen = 0;
        var itemsAdded = 0;
        var filesAdded = 0;
        var sourcesAdded = 0;
        var duplicates = 0;
        var errors = 0;

        foreach (var source in library.ScannableSources.Where(source => Directory.Exists(source.Path)))
        {
            IReadOnlyList<string> paths;
            try
            {
                paths = Directory.GetFiles(
                    source.Path,
                    "*",
                    source.IncludeSubdirectories
                        ? SearchOption.AllDirectories
                        : SearchOption.TopDirectoryOnly);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(exception, "Could not enumerate View library source {Root}", source.Path);
                errors++;
                continue;
            }

            var candidates = paths
                .Select(TryCreateCandidate)
                .Where(candidate => candidate is not null)
                .Cast<FileCandidate>()
                .ToList();
            filesSeen += candidates.Count;

            foreach (var group in BuildGroups(candidates))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var result = await IndexGroupAsync(library, source, group, ct);
                    if (result.ItemAdded) itemsAdded++;
                    filesAdded += result.FilesAdded;
                    sourcesAdded += result.SourcesAdded;
                    if (!result.ItemAdded && result.SourcesAdded > 0) duplicates++;
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    logger.LogWarning(exception, "Could not index local View item {Path}", group.Primary.Path);
                    errors++;
                }
            }
        }

        return new LocalAssetScanResultDto(
            libraryId,
            filesSeen,
            itemsAdded,
            filesAdded,
            sourcesAdded,
            duplicates,
            errors);
    }

    /// <summary>
    /// Indexes one newly arrived file through the provider-isolated View path.
    /// Adjacent Live Photo/RAW companions with the same stem are included so a
    /// direct upload produces the same logical grouping as a later full scan.
    /// </summary>
    public async Task<LocalAssetUpsertResult?> IndexPathAsync(
        Guid libraryId,
        string path,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ct.ThrowIfCancellationRequested();

        var library = FindLibrary(libraryId);
        if (library is null) return null;

        var fullPath = Path.GetFullPath(path);
        var source = library.ScannableSources.FirstOrDefault(candidate =>
            IsWithinSource(fullPath, candidate));
        if (source is null)
            throw new InvalidOperationException("The local asset path is outside the View library's configured sources.");
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("The local asset file does not exist.", fullPath);

        var uploaded = TryCreateCandidate(fullPath)
            ?? throw new InvalidDataException($"The file type '{Path.GetExtension(fullPath)}' is not supported by View.");
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidDataException("The local asset directory could not be resolved.");
        var stem = Path.GetFileNameWithoutExtension(fullPath);
        var candidates = Directory.EnumerateFiles(directory, stem + ".*", SearchOption.TopDirectoryOnly)
            .Select(TryCreateCandidate)
            .Where(candidate => candidate is not null)
            .Cast<FileCandidate>()
            .ToList();
        var group = BuildGroups(candidates).FirstOrDefault(candidate =>
            candidate.Files.Any(member => string.Equals(
                member.Candidate.Path,
                uploaded.Path,
                StringComparison.OrdinalIgnoreCase)));
        if (group is null)
            throw new InvalidDataException("The uploaded local asset could not be grouped for indexing.");

        return await IndexGroupAsync(library, source, group, ct);
    }

    private int Count(Guid libraryId, string? mediaKind, CancellationToken ct) =>
        repository.Query(new LocalAssetQuery(
            libraryId,
            Limit: 1,
            MediaKinds: mediaKind is null ? null : [mediaKind],
            IncludeHidden: true), ct).Total;

    private LibraryFolderConfig? FindLibrary(Guid libraryId) =>
        GetPersonalViewLibraries().FirstOrDefault(library => Guid.Parse(library.Id) == libraryId);

    private IEnumerable<LibraryFolderConfig> GetPersonalViewLibraries() =>
        configuration.LoadLibraries().Libraries.Where(library =>
            Guid.TryParse(library.Id, out var id)
            && id != Guid.Empty
            && string.Equals(library.Kind, LibraryKinds.Personal, StringComparison.Ordinal)
            && string.Equals(library.Area, LibraryAreas.View, StringComparison.Ordinal)
            && LibraryMetadataPolicies.BypassesExternalIdentity(library.MetadataPolicy));

    private static bool IsWithinSource(string fullPath, LibrarySourceConfig source)
    {
        var root = Path.GetFullPath(source.Path);
        var relative = Path.GetRelativePath(root, fullPath);
        if (relative == ".."
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || Path.IsPathRooted(relative))
        {
            return false;
        }

        return source.IncludeSubdirectories
            || string.Equals(Path.GetDirectoryName(fullPath), root, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<LocalAssetUpsertResult> IndexGroupAsync(
        LibraryFolderConfig library,
        LibrarySourceConfig configuredSource,
        AssetGroup group,
        CancellationToken ct)
    {
        var libraryId = Guid.Parse(library.Id);
        if (!Guid.TryParse(library.OwnerProfileId, out var ownerProfileId) || ownerProfileId == Guid.Empty)
            throw new InvalidDataException("A View library must have a configured owner profile.");
        var space = await spaces.GetByOwnerAsync(ownerProfileId, ct)
            ?? await spaces.CreateAsync(ownerProfileId, libraryId, ct);
        var metadata = ReadMetadata(group.Primary);
        var source = await ResolveSourceAsync(space, configuredSource, ct);
        var device = await ResolveDeviceAsync(space, source, configuredSource, metadata, ct);
        var registrations = new List<LocalAssetFileRegistration>(group.Files.Count);
        foreach (var member in group.Files)
        {
            var info = new FileInfo(member.Candidate.Path);
            registrations.Add(new LocalAssetFileRegistration(
                Path.GetFullPath(member.Candidate.Path),
                await HashAsync(member.Candidate.Path, ct),
                info.Name,
                member.Candidate.Type.MimeType,
                info.Length,
                new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
                member.Role,
                SourceId: source.Id,
                DeviceId: device?.Id));

            if (metadata.DurationSeconds is null
                && member.Candidate.Type.MediaKind is LocalAssetMediaKinds.Video or LocalAssetMediaKinds.Audio)
            {
                metadata = metadata with { DurationSeconds = ReadDuration(member.Candidate.Path) };
            }
        }

        return await repository.UpsertAsync(new LocalAssetRegistration(
            LibraryId: libraryId,
            PersonalSpaceId: space.Id,
            OwnerProfileId: ownerProfileId,
            MediaKind: group.Primary.Type.MediaKind,
            Title: metadata.Title ?? Path.GetFileNameWithoutExtension(group.Primary.Path),
            CapturedAt: metadata.CapturedAt,
            Files: registrations,
            Width: metadata.Width,
            Height: metadata.Height,
            DurationSeconds: metadata.DurationSeconds,
            PageCount: metadata.PageCount,
            DeviceMake: metadata.DeviceMake,
            DeviceModel: metadata.DeviceModel,
            Latitude: metadata.Latitude,
            Longitude: metadata.Longitude,
            LocationName: metadata.LocationName,
            DocumentText: metadata.DocumentText), ct);
    }

    private async Task<ViewSource> ResolveSourceAsync(
        ViewPersonalSpace space,
        LibrarySourceConfig configured,
        CancellationToken ct)
    {
        var key = string.IsNullOrWhiteSpace(configured.Id) ? Path.GetFullPath(configured.Path) : configured.Id;
        var existing = (await spaces.GetSourcesAsync(space.Id, ct))
            .FirstOrDefault(candidate => string.Equals(candidate.SourceKey, key, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return existing;
        var now = DateTimeOffset.UtcNow;
        return await spaces.UpsertSourceAsync(new ViewSource(
            Guid.Empty,
            space.Id,
            ViewSourceType.Folder,
            Path.GetFileName(Path.TrimEndingDirectorySeparator(configured.Path)),
            key,
            now,
            now,
            now), ct);
    }

    private async Task<ViewDevice?> ResolveDeviceAsync(
        ViewPersonalSpace space,
        ViewSource source,
        LibrarySourceConfig configured,
        LocalMetadata metadata,
        CancellationToken ct)
    {
        var clientId = configured.DeviceId;
        if (string.IsNullOrWhiteSpace(clientId)
            && string.IsNullOrWhiteSpace(metadata.DeviceMake)
            && string.IsNullOrWhiteSpace(metadata.DeviceModel))
            return null;
        clientId = string.IsNullOrWhiteSpace(clientId)
            ? $"metadata:{metadata.DeviceMake}:{metadata.DeviceModel}"
            : clientId;
        var existing = (await spaces.GetDevicesAsync(space.Id, ct))
            .FirstOrDefault(candidate => string.Equals(candidate.ClientDeviceId, clientId, StringComparison.Ordinal));
        if (existing is not null) return existing;
        var now = DateTimeOffset.UtcNow;
        return await spaces.UpsertDeviceAsync(new ViewDevice(
            Guid.Empty,
            space.Id,
            source.Id,
            clientId,
            metadata.DeviceModel ?? metadata.DeviceMake ?? "Imported device",
            metadata.DeviceMake,
            metadata.DeviceModel,
            null,
            ViewDeviceBackupState.Unknown,
            now,
            now), ct);
    }

    private static FileCandidate? TryCreateCandidate(string path)
    {
        var extension = Path.GetExtension(path);
        return SupportedTypes.TryGetValue(extension, out var type)
            ? new FileCandidate(Path.GetFullPath(path), extension.ToLowerInvariant(), type)
            : null;
    }

    private static IReadOnlyList<AssetGroup> BuildGroups(IReadOnlyList<FileCandidate> candidates)
    {
        var result = new List<AssetGroup>();
        foreach (var stemGroup in candidates
                     .GroupBy(
                         candidate => Path.Combine(
                             Path.GetDirectoryName(candidate.Path) ?? string.Empty,
                             Path.GetFileNameWithoutExtension(candidate.Path)),
                         StringComparer.OrdinalIgnoreCase)
                     .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            var members = stemGroup.OrderBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase).ToList();
            var consumed = new HashSet<FileCandidate>();

            var heic = members.FirstOrDefault(candidate =>
                candidate.Extension is ".heic" or ".heif");
            var motion = members.FirstOrDefault(candidate => candidate.Extension == ".mov");
            if (heic is not null && motion is not null)
            {
                result.Add(new AssetGroup(heic,
                [
                    new AssetMember(heic, LocalAssetFileRoles.Primary),
                    new AssetMember(motion, LocalAssetFileRoles.LivePhotoVideo),
                ]));
                consumed.Add(heic);
                consumed.Add(motion);
            }

            var raw = members.Where(candidate => RawExtensions.Contains(candidate.Extension)).ToList();
            var jpeg = members.Where(candidate => JpegExtensions.Contains(candidate.Extension)).ToList();
            var sidecars = members.Where(candidate => candidate.Extension == ".xmp").ToList();
            if (raw.Count > 0 && (jpeg.Count > 0 || sidecars.Count > 0))
            {
                var primary = jpeg.FirstOrDefault() ?? raw[0];
                var assetMembers = raw
                    .Select(candidate => new AssetMember(
                        candidate,
                        candidate == primary ? LocalAssetFileRoles.Primary : LocalAssetFileRoles.Raw))
                    .Concat(jpeg.Select(candidate => new AssetMember(
                        candidate,
                        candidate == primary ? LocalAssetFileRoles.Primary : LocalAssetFileRoles.Jpeg)))
                    .Concat(sidecars.Select(candidate => new AssetMember(candidate, LocalAssetFileRoles.Sidecar)))
                    .ToList();
                result.Add(new AssetGroup(primary, assetMembers));
                foreach (var candidate in raw.Concat(jpeg).Concat(sidecars)) consumed.Add(candidate);
            }

            foreach (var candidate in members.Where(candidate => !consumed.Contains(candidate)))
            {
                if (candidate.Extension == ".xmp") continue;
                result.Add(new AssetGroup(candidate,
                    [new AssetMember(candidate, LocalAssetFileRoles.Primary)]));
            }
        }

        return result;
    }

    private static LocalMetadata ReadMetadata(FileCandidate candidate)
    {
        var info = new FileInfo(candidate.Path);
        var capturedAt = ResolveFileDate(info);
        string? title = null;
        int? width = null;
        int? height = null;
        double? durationSeconds = null;
        string? deviceMake = null;
        string? deviceModel = null;
        double? latitude = null;
        double? longitude = null;

        if (candidate.Type.MediaKind == LocalAssetMediaKinds.Image)
        {
            try
            {
                using var stream = System.IO.File.OpenRead(candidate.Path);
                using var codec = SKCodec.Create(stream);
                if (codec is not null)
                {
                    width = codec.Info.Width;
                    height = codec.Info.Height;
                }
            }
            catch
            {
                // An unsupported image codec leaves dimensions unknown.
            }
        }

        try
        {
            using var file = TagLib.File.Create(candidate.Path);
            title = NullIfWhiteSpace(file.Tag.Title);
            durationSeconds = file.Properties.Duration.TotalSeconds > 0
                ? file.Properties.Duration.TotalSeconds
                : null;
            width ??= file.Properties.VideoWidth > 0 ? file.Properties.VideoWidth : null;
            height ??= file.Properties.VideoHeight > 0 ? file.Properties.VideoHeight : null;
            if (file is TagLib.Image.File image)
            {
                var tag = image.ImageTag;
                if (tag.DateTime.HasValue)
                {
                    capturedAt = new DateTimeOffset(
                        DateTime.SpecifyKind(tag.DateTime.Value, DateTimeKind.Local)).ToUniversalTime();
                }
                deviceMake = NullIfWhiteSpace(tag.Make);
                deviceModel = NullIfWhiteSpace(tag.Model);
                latitude = ValidCoordinate(tag.Latitude, -90, 90);
                longitude = ValidCoordinate(tag.Longitude, -180, 180);
            }
        }
        catch
        {
            // Unsupported or malformed local metadata is non-fatal.
        }

        return new LocalMetadata(
            title,
            capturedAt,
            width,
            height,
            durationSeconds,
            null,
            deviceMake,
            deviceModel,
            latitude,
            longitude,
            null,
            ReadBoundedDocumentText(candidate));
    }

    private static string? ReadBoundedDocumentText(FileCandidate candidate)
    {
        if (candidate.Extension is not (".txt" or ".md" or ".csv" or ".json" or ".xml"))
        {
            return null;
        }

        try
        {
            using var stream = new FileStream(
                candidate.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
            var buffer = new char[MaxDocumentCharacters];
            var count = reader.ReadBlock(buffer, 0, buffer.Length);
            return count == 0 ? null : new string(buffer, 0, count);
        }
        catch
        {
            return null;
        }
    }

    private static double? ReadDuration(string path)
    {
        try
        {
            using var file = TagLib.File.Create(path);
            return file.Properties.Duration.TotalSeconds > 0
                ? file.Properties.Duration.TotalSeconds
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string> HashAsync(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexStringLower(hash);
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

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record FileType(string MediaKind, string MimeType);
    private sealed record FileCandidate(string Path, string Extension, FileType Type);
    private sealed record AssetMember(FileCandidate Candidate, string Role);
    private sealed record AssetGroup(FileCandidate Primary, IReadOnlyList<AssetMember> Files);
    private sealed record LocalMetadata(
        string? Title,
        DateTimeOffset CapturedAt,
        int? Width,
        int? Height,
        double? DurationSeconds,
        int? PageCount,
        string? DeviceMake,
        string? DeviceModel,
        double? Latitude,
        double? Longitude,
        string? LocationName,
        string? DocumentText);
}
