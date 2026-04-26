using System.Text.Json.Serialization;

namespace MediaEngine.Api.Models;

public sealed class CollectionItemAddRequest
{
    [JsonPropertyName("work_id")]
    public Guid WorkId { get; init; }
}

public sealed class CollectionItemReorderRequest
{
    [JsonPropertyName("item_ids")]
    public List<Guid> ItemIds { get; init; } = [];
}
