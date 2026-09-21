namespace MediaEngine.Contracts.Artwork;

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
    bool UsesLegacyPersonImage);
