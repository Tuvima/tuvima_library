namespace MediaEngine.Contracts.Artwork;

public enum ArtworkResolutionMode
{
    None,
    Explicit,
    AutomaticGroup,
}

public sealed record ArtworkLibraryPreviewItemDto(
    Guid WorkId,
    Guid? AssetId,
    string Title,
    string ImageUrl,
    string Shape,
    string? Position,
    string? MediaType);

public sealed record ArtworkBrowsePageDto(
    IReadOnlyList<ArtworkLibraryItemDto> Items,
    int Offset,
    int Limit,
    int Total)
{
    public bool HasMore => Offset + Items.Count < Total;
}

public sealed record ArtworkLibraryItemDto(
    Guid EntityId,
    string EntityType,
    string DisplayTitle,
    string? MediaType,
    string? Year,
    string? Subtitle,
    string? ImageUrl,
    string? PrimaryAssetType,
    IReadOnlyList<string> AssetTypes,
    int VariantCount,
    int OwnedWorkCount,
    bool UsesLegacyPersonImage)
{
    public bool IsStructural { get; init; }
    public string? GroupKind { get; init; }
    public ArtworkResolutionMode ResolutionMode { get; init; }
    public IReadOnlyList<ArtworkLibraryPreviewItemDto> PreviewItems { get; init; } = [];
    public string? BackgroundImageUrl { get; init; }
    public string? LogoImageUrl { get; init; }
    public string? CanonicalId { get; init; }
}

public sealed record ArtworkAssetContextDto(
    Guid EntityId,
    string EntityType,
    string EntityLabel,
    string? MediaType,
    string? Year,
    string? Role,
    string? Provider);

public sealed record ArtworkAssetDto(
    Guid Id,
    string ContentUrl,
    string ThumbnailUrl,
    int? Width,
    int? Height,
    string Aspect,
    string? SourceProvider,
    string? SourceUrl,
    IReadOnlyList<ArtworkAssetContextDto> Contexts,
    bool AlreadyLinked = false);

public sealed record ArtworkAssetPageDto(
    IReadOnlyList<ArtworkAssetDto> Items,
    int Offset,
    int Limit,
    int Total)
{
    public bool HasMore => Offset + Items.Count < Total;
}

public sealed record ArtworkEntityVariantDto(
    Guid LinkId,
    Guid ArtworkAssetId,
    string Role,
    string? Context,
    string? SourceAssetType,
    bool IsPreferred,
    bool IsUserOverride,
    string ContentUrl,
    string ThumbnailUrl,
    int? Width,
    int? Height,
    string Aspect,
    string? SourceProvider,
    string? SourceUrl);

public sealed record ArtworkEntityWorkspaceDto(
    Guid EntityId,
    string EntityType,
    IReadOnlyList<ArtworkEntityVariantDto> Variants);

public sealed record ArtworkLinkRequest(
    Guid ArtworkAssetId,
    string Role,
    string? Context = null,
    bool Preferred = true,
    string? EntityLabel = null,
    string? MediaType = null,
    string? Year = null);

public sealed record ArtworkFromUrlRequest(
    string Url,
    string Role,
    string? Context = null,
    bool Preferred = true,
    string? EntityLabel = null,
    string? MediaType = null,
    string? Year = null);
