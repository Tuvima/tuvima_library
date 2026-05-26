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

public sealed class ProviderArtworkRefreshDto
{
    public string Provider { get; set; } = "fanart_tv";
    public string ProviderName { get; set; } = "Fanart.tv";
    public string Status { get; set; } = string.Empty;
    public bool Success { get; set; }
    public bool Skipped { get; set; }
    public string? SkippedReason { get; set; }
    public string? Message { get; set; }
    public string? MediaType { get; set; }
    public string? BridgeKey { get; set; }
    public string? BridgeId { get; set; }
    public string? Endpoint { get; set; }
    public int? HttpStatusCode { get; set; }
    public int DownloadedCount { get; set; }
    public int UpdatedPreferredCount { get; set; }
    public Dictionary<string, int> StoredVariantCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Diagnostics { get; set; } = [];
    public DateTimeOffset LastCheckedAt { get; set; }
}
