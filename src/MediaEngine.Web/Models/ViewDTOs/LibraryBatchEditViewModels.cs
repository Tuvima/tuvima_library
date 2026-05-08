using System.Text.Json.Serialization;

namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>Result of a batch edit operation from POST /library/batch-edit.</summary>
public sealed class LibraryBatchEditResultViewModel
{
    [JsonPropertyName("updated_count")]
    public int UpdatedCount { get; init; }

    [JsonPropertyName("failed_ids")]
    public List<Guid> FailedIds { get; init; } = [];

    [JsonPropertyName("errors")]
    public List<string> Errors { get; init; } = [];
}
