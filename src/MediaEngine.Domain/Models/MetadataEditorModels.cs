namespace MediaEngine.Domain.Models;

public sealed record MetadataReclassifyTarget(Guid TargetAssetId, Guid? WorkId);

public sealed record MetadataEditorAssetSample(
    Guid AssetId,
    string? FilePath,
    string? WritebackStatus);

public sealed record MetadataEditorLaunchContext(
    Guid LaunchEntityId,
    string LaunchEntityKind,
    Guid WorkId,
    Guid? ParentWorkId,
    Guid RootWorkId,
    string MediaType,
    string WorkKind,
    Guid? RepresentativeAssetId,
    string? RepresentativeMediaFilePath,
    string? RepresentativeWritebackStatus);

public sealed record MetadataArtworkResolutionContext(
    Guid RequestedEntityId,
    Guid? WorkId,
    Guid? RootWorkId,
    Guid? PrimaryAssetId,
    Guid? RootPrimaryAssetId,
    IReadOnlyList<Guid> ArtworkEntityIds,
    Guid? PreferredArtworkEntityId);
