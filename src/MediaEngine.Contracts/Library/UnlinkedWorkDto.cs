using System.Text.Json.Serialization;

namespace MediaEngine.Contracts.Library;

public sealed class UnlinkedWorkDto
{
    [JsonPropertyName("work_id")] public Guid WorkId { get; init; }
    [JsonPropertyName("entity_id")] public Guid EntityId { get; init; }
    [JsonPropertyName("title")] public string Title { get; init; } = "";
    [JsonPropertyName("media_type")] public string MediaType { get; init; } = "";
    [JsonPropertyName("wikidata_qid")] public string WikidataQid { get; init; } = "";
}
