using System.Text.Json.Serialization;

namespace MediaEngine.Application.ReadModels;

public sealed class PersonAliasResponse
{
    [JsonPropertyName("person_id")]
    public Guid PersonId { get; init; }

    [JsonPropertyName("person_name")]
    public string PersonName { get; init; } = string.Empty;

    [JsonPropertyName("is_pseudonym")]
    public bool IsPseudonym { get; init; }

    [JsonPropertyName("aliases")]
    public IReadOnlyList<PersonAliasItemResponse> Aliases { get; init; } = [];
}

public sealed class PersonAliasItemResponse
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("roles")]
    public IReadOnlyList<string> Roles { get; init; } = [];

    [JsonPropertyName("headshot_url")]
    public string? HeadshotUrl { get; init; }

    [JsonPropertyName("is_pseudonym")]
    public bool IsPseudonym { get; init; }

    [JsonPropertyName("wikidata_qid")]
    public string? WikidataQid { get; init; }

    [JsonPropertyName("relationship")]
    public string Relationship { get; init; } = string.Empty;
}

public sealed class PersonSummaryResponse
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("roles")]
    public IReadOnlyList<string> Roles { get; init; } = [];

    [JsonPropertyName("wikidata_qid")]
    public string? WikidataQid { get; init; }

    [JsonPropertyName("headshot_url")]
    public string? HeadshotUrl { get; init; }

    [JsonPropertyName("has_local_headshot")]
    public bool HasLocalHeadshot { get; init; }

    [JsonPropertyName("biography")]
    public string? Biography { get; init; }

    [JsonPropertyName("occupation")]
    public string? Occupation { get; init; }
}
