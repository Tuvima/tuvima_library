using System.Text.Json.Serialization;

namespace MediaEngine.Contracts.Metadata;

public sealed class ArtworkEditorDto
{
    public ArtworkEditorDto() { }

    public ArtworkEditorDto(Guid entityId, IReadOnlyList<ArtworkSlotDto> slots)
    {
        EntityId = entityId;
        Slots = slots.ToList();
    }

    [JsonPropertyName("entity_id")]
    public Guid EntityId { get; set; }

    [JsonPropertyName("slots")]
    public List<ArtworkSlotDto> Slots { get; set; } = [];
}

public sealed class ArtworkSlotDto
{
    public ArtworkSlotDto() { }

    public ArtworkSlotDto(string assetType, IReadOnlyList<ArtworkVariantDto> variants)
    {
        AssetType = assetType;
        Variants = variants.ToList();
    }

    [JsonPropertyName("asset_type")]
    public string AssetType { get; set; } = string.Empty;

    [JsonPropertyName("variants")]
    public List<ArtworkVariantDto> Variants { get; set; } = [];
}

public sealed class ArtworkVariantDto
{
    public ArtworkVariantDto() { }

    public ArtworkVariantDto(
        Guid Id,
        string AssetType,
        string? ImageUrl,
        bool IsPreferred,
        string Origin,
        string? ProviderName,
        bool CanDelete,
        DateTimeOffset? CreatedAt)
    {
        this.Id = Id;
        this.AssetType = AssetType;
        this.ImageUrl = ImageUrl;
        this.IsPreferred = IsPreferred;
        this.Origin = Origin;
        this.ProviderName = ProviderName;
        this.CanDelete = CanDelete;
        this.CreatedAt = CreatedAt;
    }

    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("asset_type")]
    public string AssetType { get; set; } = string.Empty;

    [JsonPropertyName("image_url")]
    public string? ImageUrl { get; set; }

    [JsonPropertyName("is_preferred")]
    public bool IsPreferred { get; set; }

    [JsonPropertyName("origin")]
    public string Origin { get; set; } = "Stored";

    [JsonPropertyName("provider_name")]
    public string? ProviderName { get; set; }

    [JsonPropertyName("can_delete")]
    public bool CanDelete { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }
}

public sealed class ProviderArtworkRefreshDto
{
    public ProviderArtworkRefreshDto() { }

    public ProviderArtworkRefreshDto(
        string Provider,
        string ProviderName,
        string Status,
        bool Success,
        bool Skipped,
        string? SkippedReason,
        string? Message,
        string? MediaType,
        string? BridgeKey,
        string? BridgeId,
        string? Endpoint,
        int? HttpStatusCode,
        int DownloadedCount,
        int UpdatedPreferredCount,
        IReadOnlyDictionary<string, int> StoredVariantCounts,
        IReadOnlyList<string> Diagnostics,
        DateTimeOffset LastCheckedAt)
    {
        this.Provider = Provider;
        this.ProviderName = ProviderName;
        this.Status = Status;
        this.Success = Success;
        this.Skipped = Skipped;
        this.SkippedReason = SkippedReason;
        this.Message = Message;
        this.MediaType = MediaType;
        this.BridgeKey = BridgeKey;
        this.BridgeId = BridgeId;
        this.Endpoint = Endpoint;
        this.HttpStatusCode = HttpStatusCode;
        this.DownloadedCount = DownloadedCount;
        this.UpdatedPreferredCount = UpdatedPreferredCount;
        this.StoredVariantCounts = new(StoredVariantCounts, StringComparer.OrdinalIgnoreCase);
        this.Diagnostics = Diagnostics.ToList();
        this.LastCheckedAt = LastCheckedAt;
    }

    [JsonPropertyName("provider")]
    public string Provider { get; set; } = "fanart_tv";

    [JsonPropertyName("provider_name")]
    public string ProviderName { get; set; } = "Fanart.tv";

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("skipped")]
    public bool Skipped { get; set; }

    [JsonPropertyName("skipped_reason")]
    public string? SkippedReason { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("media_type")]
    public string? MediaType { get; set; }

    [JsonPropertyName("bridge_key")]
    public string? BridgeKey { get; set; }

    [JsonPropertyName("bridge_id")]
    public string? BridgeId { get; set; }

    [JsonPropertyName("endpoint")]
    public string? Endpoint { get; set; }

    [JsonPropertyName("http_status_code")]
    public int? HttpStatusCode { get; set; }

    [JsonPropertyName("downloaded_count")]
    public int DownloadedCount { get; set; }

    [JsonPropertyName("updated_preferred_count")]
    public int UpdatedPreferredCount { get; set; }

    [JsonPropertyName("stored_variant_counts")]
    public Dictionary<string, int> StoredVariantCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("diagnostics")]
    public List<string> Diagnostics { get; set; } = [];

    [JsonPropertyName("last_checked_at")]
    public DateTimeOffset LastCheckedAt { get; set; }
}
