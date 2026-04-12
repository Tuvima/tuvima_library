using System.Text.Json.Serialization;

namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>Result of a batch edit operation from GET /vault/batch-edit.</summary>
public sealed class VaultBatchEditResultViewModel
{
    [JsonPropertyName("updated_count")]
    public int UpdatedCount { get; init; }

    [JsonPropertyName("failed_ids")]
    public List<Guid> FailedIds { get; init; } = [];

    [JsonPropertyName("errors")]
    public List<string> Errors { get; init; } = [];
}
