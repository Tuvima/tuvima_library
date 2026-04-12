using System.Text.Json.Serialization;

namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>A work with universe-related QIDs but no hub assignment.</summary>
public sealed class UniverseCandidateViewModel
{
    [JsonPropertyName("work_id")] public Guid WorkId { get; init; }
    [JsonPropertyName("entity_id")] public Guid EntityId { get; init; }
    [JsonPropertyName("title")] public string Title { get; init; } = "";
    [JsonPropertyName("media_type")] public string MediaType { get; init; } = "";
    [JsonPropertyName("candidate_qid")] public string CandidateQid { get; init; } = "";
    [JsonPropertyName("candidate_type")] public string CandidateType { get; init; } = "";
    [JsonPropertyName("candidate_label")] public string CandidateLabel { get; init; } = "";
}

/// <summary>A work with a Wikidata QID but no universe-related properties.</summary>
public sealed class UnlinkedWorkViewModel
{
    [JsonPropertyName("work_id")] public Guid WorkId { get; init; }
    [JsonPropertyName("entity_id")] public Guid EntityId { get; init; }
    [JsonPropertyName("title")] public string Title { get; init; } = "";
    [JsonPropertyName("media_type")] public string MediaType { get; init; } = "";
    [JsonPropertyName("wikidata_qid")] public string WikidataQid { get; init; } = "";
}
