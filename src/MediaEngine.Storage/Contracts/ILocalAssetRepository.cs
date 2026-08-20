using MediaEngine.Contracts.LocalAssets;

namespace MediaEngine.Storage.Contracts;

/// <summary>
/// Persistence boundary for provider-isolated personal-library assets.
/// SQLite operations are synchronous; only serialized write-lock acquisition
/// is asynchronous through <see cref="IDatabaseConnection.ExecuteWriteAsync{T}"/>.
/// </summary>
public interface ILocalAssetRepository
{
    LocalAssetPageDto Query(LocalAssetQuery query, CancellationToken ct = default);
    LocalAssetDto? Find(Guid itemId, CancellationToken ct = default);
    LocalAssetContentLocation? ResolveContent(
        Guid itemId,
        string role = LocalAssetFileRoles.Primary,
        CancellationToken ct = default);
    IReadOnlyList<LocalCollectionDto> GetCollections(Guid libraryId, CancellationToken ct = default);

    Task<LocalAssetUpsertResult> UpsertAsync(
        LocalAssetRegistration registration,
        CancellationToken ct = default);
    Task<bool> SetFlagsAsync(
        Guid itemId,
        bool? favorite,
        bool? hidden,
        CancellationToken ct = default);
    Task ReplaceTagsAsync(
        Guid itemId,
        IReadOnlyCollection<string> tags,
        CancellationToken ct = default);
    Task<LocalCollectionDto> CreateCollectionAsync(
        Guid libraryId,
        string name,
        string? description,
        string collectionKind,
        CancellationToken ct = default);
    Task<int> AddToCollectionAsync(
        Guid collectionId,
        IReadOnlyCollection<Guid> itemIds,
        CancellationToken ct = default);
    Task<Guid> AddAnnotationAsync(
        Guid itemId,
        LocalAssetAnnotation annotation,
        CancellationToken ct = default);
}

public sealed record LocalAssetQuery(
    Guid LibraryId,
    int Offset = 0,
    int Limit = 50,
    string? Search = null,
    IReadOnlyCollection<string>? MediaKinds = null,
    bool FavoritesOnly = false,
    bool IncludeHidden = false,
    bool HiddenOnly = false,
    Guid? CollectionId = null);

public sealed record LocalAssetRegistration(
    Guid LibraryId,
    string MediaKind,
    string? Title,
    DateTimeOffset? CapturedAt,
    IReadOnlyList<LocalAssetFileRegistration> Files,
    int? Width = null,
    int? Height = null,
    double? DurationSeconds = null,
    int? PageCount = null,
    string? DeviceMake = null,
    string? DeviceModel = null,
    double? Latitude = null,
    double? Longitude = null,
    string? LocationName = null,
    string? DocumentText = null,
    string? MetadataJson = null,
    IReadOnlyCollection<string>? Tags = null,
    Guid? ExistingItemId = null);

public sealed record LocalAssetFileRegistration(
    string FilePath,
    string ContentHash,
    string FileName,
    string MimeType,
    long ByteSize,
    DateTimeOffset ModifiedAt,
    string Role = LocalAssetFileRoles.Primary,
    string? DerivativeKind = null);

public sealed record LocalAssetUpsertResult(
    Guid ItemId,
    bool ItemAdded,
    int FilesAdded,
    int SourcesAdded);

public sealed record LocalAssetContentLocation(
    Guid ItemId,
    Guid FileId,
    Guid LibraryId,
    string FilePath,
    string MimeType,
    long ByteSize,
    string ContentHash,
    string Role,
    string? DerivativeKind);

public sealed record LocalAssetAnnotation(
    string Kind,
    string Value,
    string Source,
    double? Confidence = null,
    string? ModelName = null,
    string? ModelVersion = null,
    string? ProvenanceJson = null,
    DateTimeOffset? ReviewedAt = null);

public static class LocalAssetMediaKinds
{
    public const string Image = "image";
    public const string Video = "video";
    public const string Document = "document";
    public const string Audio = "audio";
    public const string Other = "other";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Image,
        Video,
        Document,
        Audio,
        Other,
    };
}

public static class LocalAssetFileRoles
{
    public const string Primary = "primary";
    public const string Original = "original";
    public const string LivePhotoVideo = "live_photo_video";
    public const string Raw = "raw";
    public const string Jpeg = "jpeg";
    public const string Sidecar = "sidecar";
    public const string AudioCompanion = "audio_companion";
    public const string Derivative = "derivative";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Primary,
        Original,
        LivePhotoVideo,
        Raw,
        Jpeg,
        Sidecar,
        AudioCompanion,
        Derivative,
    };
}
