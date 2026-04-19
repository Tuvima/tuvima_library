namespace MediaEngine.Web.Models.ViewDTOs;

public sealed class ArtworkEditorDto
{
    public Guid EntityId { get; set; }
    public List<ArtworkSlotDto> Slots { get; set; } = [];
}

public sealed class ArtworkSlotDto
{
    public string AssetType { get; set; } = string.Empty;
    public List<ArtworkVariantDto> Variants { get; set; } = [];
}

public sealed class ArtworkVariantDto
{
    public Guid Id { get; set; }
    public string AssetType { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public bool IsPreferred { get; set; }
    public string Origin { get; set; } = "Stored";
    public string? ProviderName { get; set; }
    public bool CanDelete { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}
