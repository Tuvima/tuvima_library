using System.Text.Json.Serialization;

namespace MediaEngine.Api.Models;

public sealed class UniverseCandidateDto
{
    [JsonPropertyName("work_id")] public Guid WorkId { get; init; }
    [JsonPropertyName("entity_id")] public Guid EntityId { get; init; }
    [JsonPropertyName("title")] public string Title { get; init; } = "";
    [JsonPropertyName("media_type")] public string MediaType { get; init; } = "";
    [JsonPropertyName("candidate_qid")] public string CandidateQid { get; init; } = "";
    [JsonPropertyName("candidate_type")] public string CandidateType { get; init; } = "";
    [JsonPropertyName("candidate_label")] public string CandidateLabel { get; init; } = "";
}

public sealed class UnlinkedWorkDto
{
    [JsonPropertyName("work_id")] public Guid WorkId { get; init; }
    [JsonPropertyName("entity_id")] public Guid EntityId { get; init; }
    [JsonPropertyName("title")] public string Title { get; init; } = "";
    [JsonPropertyName("media_type")] public string MediaType { get; init; } = "";
    [JsonPropertyName("wikidata_qid")] public string WikidataQid { get; init; } = "";
}

public sealed class UniverseAcceptRequest
{
    [JsonPropertyName("target_hub_qid")] public required string TargetHubQid { get; init; }
}

public sealed class UniverseBatchAcceptRequest
{
    [JsonPropertyName("work_ids")] public List<Guid> WorkIds { get; init; } = [];
}

public sealed class UniverseManualAssignRequest
{
    [JsonPropertyName("work_id")] public Guid WorkId { get; init; }
    [JsonPropertyName("hub_id")] public Guid HubId { get; init; }
}
