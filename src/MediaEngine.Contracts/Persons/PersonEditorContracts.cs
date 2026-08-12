using System.Text.Json.Serialization;
using MediaEngine.Contracts.Items;

namespace MediaEngine.Contracts.Persons;

public sealed class PersonEditorStateResponse
{
    [JsonPropertyName("person_id")] public Guid PersonId { get; init; }
    [JsonPropertyName("baseline_name")] public string BaselineName { get; init; } = string.Empty;
    [JsonPropertyName("baseline_biography")] public string? BaselineBiography { get; init; }
    [JsonPropertyName("display_overrides")] public IReadOnlyDictionary<string, string> DisplayOverrides { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    [JsonPropertyName("local_tags")] public IReadOnlyList<string> LocalTags { get; init; } = [];
    [JsonPropertyName("revision")] public long Revision { get; init; }
    [JsonPropertyName("updated_at")] public DateTimeOffset? UpdatedAt { get; init; }
    [JsonPropertyName("history")] public IReadOnlyList<LibraryItemHistoryDto> History { get; init; } = [];
}

public sealed class PersonEditorSaveRequest
{
    [JsonPropertyName("profile_id")] public Guid? ProfileId { get; init; }
    [JsonPropertyName("expected_revision")] public long ExpectedRevision { get; init; }
    [JsonPropertyName("display_overrides")] public Dictionary<string, string> DisplayOverrides { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    [JsonPropertyName("local_tags")] public List<string> LocalTags { get; init; } = [];
}

public sealed record PersonEditorSaveResponse(
    [property: JsonPropertyName("person_id")] Guid PersonId,
    [property: JsonPropertyName("revision")] long Revision);
