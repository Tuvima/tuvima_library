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

public sealed record ArtworkLibraryPageDto(
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
}
