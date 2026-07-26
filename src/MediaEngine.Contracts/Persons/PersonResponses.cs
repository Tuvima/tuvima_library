using System.Text.Json.Serialization;

namespace MediaEngine.Contracts.Persons;

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

public sealed class PersonDetailResponse
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

    [JsonPropertyName("biography")]
    public string? Biography { get; init; }

    [JsonPropertyName("occupation")]
    public string? Occupation { get; init; }

    [JsonPropertyName("date_of_birth")]
    public string? DateOfBirth { get; init; }

    [JsonPropertyName("date_of_death")]
    public string? DateOfDeath { get; init; }

    [JsonPropertyName("place_of_birth")]
    public string? PlaceOfBirth { get; init; }

    [JsonPropertyName("place_of_death")]
    public string? PlaceOfDeath { get; init; }

    [JsonPropertyName("nationality")]
    public string? Nationality { get; init; }

    [JsonPropertyName("instagram")]
    public string? Instagram { get; init; }

    [JsonPropertyName("twitter")]
    public string? Twitter { get; init; }

    [JsonPropertyName("tiktok")]
    public string? TikTok { get; init; }

    [JsonPropertyName("mastodon")]
    public string? Mastodon { get; init; }

    [JsonPropertyName("website")]
    public string? Website { get; init; }

    [JsonPropertyName("has_local_headshot")]
    public bool HasLocalHeadshot { get; init; }

    [JsonPropertyName("is_pseudonym")]
    public bool IsPseudonym { get; init; }

    [JsonPropertyName("is_group")]
    public bool IsGroup { get; init; }

    [JsonPropertyName("group_members")]
    public IReadOnlyList<PersonGroupMemberDto> GroupMembers { get; init; } = [];

    [JsonPropertyName("member_of_groups")]
    public IReadOnlyList<PersonGroupMemberDto> MemberOfGroups { get; init; } = [];

    [JsonPropertyName("banner_url")]
    public string? BannerUrl { get; init; }

    [JsonPropertyName("background_url")]
    public string? BackgroundUrl { get; init; }

    [JsonPropertyName("logo_url")]
    public string? LogoUrl { get; init; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("enriched_at")]
    public DateTimeOffset? EnrichedAt { get; init; }
}
