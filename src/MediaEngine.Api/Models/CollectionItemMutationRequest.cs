using System.Text.Json.Serialization;

namespace MediaEngine.Api.Models;

public sealed class CollectionItemAddRequest
{
    [JsonPropertyName("work_id")]
    public Guid WorkId { get; init; }
}
