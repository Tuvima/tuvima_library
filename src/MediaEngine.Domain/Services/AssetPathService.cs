using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;

namespace MediaEngine.Domain.Services;

/// <summary>
/// Central policy-driven resolver for managed asset storage.
/// </summary>
public sealed class AssetPathService
{
    private readonly LibraryStoragePolicy _policy;

    public AssetPathService(string libraryRoot, LibraryStoragePolicy? policy = null)
    {
        LibraryRoot = string.IsNullOrWhiteSpace(libraryRoot)
            ? Path.Combine(Path.GetTempPath(), "tuvima_library_unset")
            : Path.GetFullPath(libraryRoot);

        _policy = policy ?? new LibraryStoragePolicy();
    }

    public string LibraryRoot { get; }

    public LibraryStoragePolicy Policy => _policy;

    public string DataRoot => Path.Combine(LibraryRoot, ".data");

    public string AssetsRoot => Path.Combine(DataRoot, "assets");

    public string ArtworkRoot => Path.Combine(AssetsRoot, "artwork");

    public string DerivedRoot => Path.Combine(AssetsRoot, "derived");

    public string MetadataRoot => Path.Combine(AssetsRoot, "metadata");

    public string TranscriptsRoot => Path.Combine(AssetsRoot, "transcripts");

    public string SubtitleCacheRoot => Path.Combine(AssetsRoot, "subtitle-cache");

    public string TextTracksRoot => Path.Combine(AssetsRoot, "text-tracks");

    public string PeopleRoot => Path.Combine(AssetsRoot, "people");

    public string LegacyImagesRoot => Path.Combine(DataRoot, "images");

    public bool IsHybridMode => _policy.Mode == StorageMode.Hybrid;

    public bool IsArtworkCentralized => _policy.Mode != StorageMode.CoLocated;

    public bool ShouldExportArtwork => _policy.Mode == StorageMode.CoLocated || _policy.ArtworkExport || _policy.ExportProfile.Artwork;

    public bool ShouldKeepPreferredSubtitlesLocal => _policy.Mode != StorageMode.Centralized || _policy.SubtitleExport || _policy.ExportProfile.PreferredSubtitles;

    public bool ShouldExportMetadataSidecars => _policy.MetadataSidecarExport || _policy.ExportProfile.MetadataSidecars;

    public string GetCentralAssetPath(
        string ownerKind,
        Guid ownerId,
        string assetType,
        Guid variantId,
        string extension) =>
        GetCentralAssetPath(ownerKind, ownerId.ToString("D"), assetType, variantId, extension);

    public string GetCentralAssetPath(
        string ownerKind,
        string ownerKey,
        string assetType,
        Guid variantId,
        string extension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(assetType);
        if (variantId == Guid.Empty)
            throw new ArgumentException("Variant id is required.", nameof(variantId));

        return Path.Combine(
            ArtworkRoot,
            NormalizeSegment(ownerKind),
            NormalizeSegment(ownerKey),
            NormalizeAssetTypeSegment(assetType),
            $"{variantId:N}{NormalizeExtension(extension)}");
    }

    public string GetCentralDerivedPath(
        string ownerKind,
        Guid ownerId,
        string artifactType,
        string fileName) =>
        GetCentralDerivedPath(ownerKind, ownerId.ToString("D"), artifactType, fileName);

    public string GetCentralDerivedPath(
        string ownerKind,
        string ownerKey,
        string artifactType,
        string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactType);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        return Path.Combine(
            DerivedRoot,
            NormalizeSegment(ownerKind),
            NormalizeSegment(ownerKey),
            NormalizeSegment(artifactType),
            fileName);
    }

    public string GetPersonRoot(Guid personId)
    {
        if (personId == Guid.Empty)
            throw new ArgumentException("Person id is required.", nameof(personId));

        return Path.Combine(PeopleRoot, personId.ToString("D"));
    }

    public string GetPersonHeadshotPath(Guid personId, string extension = ".jpg") =>
        Path.Combine(GetPersonRoot(personId), "headshot" + NormalizeExtension(extension));

    public string GetCharacterPortraitPath(Guid personId, Guid fictionalEntityId, string extension = ".jpg")
    {
        if (fictionalEntityId == Guid.Empty)
            throw new ArgumentException("Fictional entity id is required.", nameof(fictionalEntityId));

        return Path.Combine(
            GetPersonRoot(personId),
            "characters",
            fictionalEntityId.ToString("D"),
            "portrait" + NormalizeExtension(extension));
    }

    public string GetLocalSidecarPath(string mediaFilePath, string assetType, string extension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaFilePath);
        return assetType switch
        {
            "Subtitle" => BuildSubtitleSidecarPath(mediaFilePath, extension),
            "Lyrics" => BuildLyricsSidecarPath(mediaFilePath, extension),
            _ => BuildArtworkExportPath(mediaFilePath, assetType, extension),
        };
    }

    public string GetCentralTextTrackPath(Guid assetId, string kind, string provider, string language, string extension)
    {
        if (assetId == Guid.Empty)
            throw new ArgumentException("Asset id is required.", nameof(assetId));
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(language);

        var root = kind.Equals("Lyrics", StringComparison.OrdinalIgnoreCase)
            ? "lyrics"
            : "subtitles";

        return Path.Combine(
            TextTracksRoot,
            root,
            assetId.ToString("D"),
            $"{NormalizeSegment(provider)}-{NormalizeSegment(language)}{NormalizeExtension(extension)}");
    }

    public string GetExportedSidecarPath(string ownerPath, string assetType, string extension, Guid? variantId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerPath);
        if (Directory.Exists(ownerPath))
            return BuildFolderArtworkExportPath(ownerPath, assetType, extension, variantId);

        return BuildArtworkExportPath(ownerPath, assetType, extension, variantId);
    }

    public static void EnsureDirectory(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
    }

    private static string BuildArtworkExportPath(string mediaFilePath, string assetType, string extension, Guid? variantId = null)
    {
        var normalizedExtension = NormalizeExtension(extension);

        if (variantId.HasValue && variantId.Value != Guid.Empty)
            return ImagePathService.GetMediaFileArtworkVariantPath(mediaFilePath, assetType, variantId.Value, normalizedExtension);

        return assetType switch
        {
            "CoverArt" => ImagePathService.GetMediaFilePosterPath(mediaFilePath),
            "Background" => ImagePathService.GetMediaFileFanartPath(mediaFilePath),
            "Banner" => ImagePathService.GetMediaFileBannerPath(mediaFilePath),
            "Logo" => ImagePathService.GetMediaFileLogoPath(mediaFilePath),
            "DiscArt" => ImagePathService.GetMediaFileDiscArtPath(mediaFilePath),
            "ClearArt" => ImagePathService.GetMediaFileClearArtPath(mediaFilePath),
            "SquareArt" => ImagePathService.GetMediaFileSquareArtPath(mediaFilePath),
            "EpisodeStill" => ImagePathService.GetMediaFileThumbPath(mediaFilePath),
            _ => throw new ArgumentOutOfRangeException(nameof(assetType), assetType, "Unsupported export artwork type."),
        };
    }

    private static string BuildFolderArtworkExportPath(string folderPath, string assetType, string extension, Guid? variantId = null)
    {
        var normalizedExtension = NormalizeExtension(extension);
        var canonicalName = assetType switch
        {
            "CoverArt" => "poster",
            "Background" => "fanart",
            "Banner" => "banner",
            "Logo" => "logo",
            "DiscArt" => "discart",
            "ClearArt" => "clearart",
            "SquareArt" => "square",
            "SeasonPoster" => "poster",
            "SeasonThumb" => "thumb",
            _ => throw new ArgumentOutOfRangeException(nameof(assetType), assetType, "Unsupported folder artwork type."),
        };

        return variantId.HasValue && variantId.Value != Guid.Empty
            ? Path.Combine(folderPath, $"{canonicalName}-{variantId.Value:N}{normalizedExtension}")
            : Path.Combine(folderPath, $"{canonicalName}{normalizedExtension}");
    }

    private static string BuildSubtitleSidecarPath(string mediaFilePath, string extension)
    {
        var normalizedExtension = NormalizeExtension(extension);
        var directory = Path.GetDirectoryName(mediaFilePath) ?? ".";
        var basename = Path.GetFileNameWithoutExtension(mediaFilePath);
        return Path.Combine(directory, $"{basename}{normalizedExtension}");
    }

    public static string BuildSubtitleSidecarPath(string mediaFilePath, string language, string extension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaFilePath);
        var normalizedExtension = NormalizeExtension(extension);
        var directory = Path.GetDirectoryName(mediaFilePath) ?? ".";
        var basename = Path.GetFileNameWithoutExtension(mediaFilePath);
        var suffix = string.IsNullOrWhiteSpace(language) ? "und" : NormalizeSegment(language);
        return Path.Combine(directory, $"{basename}.{suffix}{normalizedExtension}");
    }

    private static string BuildLyricsSidecarPath(string mediaFilePath, string extension)
    {
        var normalizedExtension = NormalizeExtension(extension);
        var directory = Path.GetDirectoryName(mediaFilePath) ?? ".";
        var basename = Path.GetFileNameWithoutExtension(mediaFilePath);
        return Path.Combine(directory, $"{basename}{normalizedExtension}");
    }

    private static string NormalizeAssetTypeSegment(string assetType) =>
        assetType.Trim().ToLowerInvariant() switch
        {
            "coverart" => "cover",
            "background" => "background",
            "banner" => "banner",
            "squareart" => "square",
            "logo" => "logo",
            "discart" => "discart",
            "clearart" => "clearart",
            "seasonposter" => "season-poster",
            "seasonthumb" => "season-thumb",
            "episodestill" => "episode-still",
            _ => NormalizeSegment(assetType),
        };

    private static string NormalizeSegment(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var filtered = new string(value.Trim().ToLowerInvariant().Select(ch => invalidChars.Contains(ch) ? '-' : ch).ToArray());
        return string.IsNullOrWhiteSpace(filtered) ? "unknown" : filtered.Replace(' ', '-');
    }

    private static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            throw new ArgumentException("File extension is required.", nameof(extension));

        return extension.StartsWith(".", StringComparison.Ordinal) ? extension : "." + extension;
    }
}
