using System.Text.Json.Serialization;

namespace MediaEngine.Contracts.Items;

public sealed class ItemPreferencesRequest
{
    [JsonPropertyName("fields")]
    public Dictionary<string, string> Fields { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ItemPreferencesResponse
{
    [JsonPropertyName("entity_id")] public Guid EntityId { get; init; }
    [JsonPropertyName("fields_updated")] public int FieldsUpdated { get; init; }
    [JsonPropertyName("updated_keys")] public IReadOnlyList<string> UpdatedKeys { get; init; } = [];
    [JsonPropertyName("message")] public string Message { get; init; } = "";
}

public sealed class ItemDisplayOverridesRequest
{
    [JsonPropertyName("fields")]
    public Dictionary<string, string> Fields { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ItemDisplayOverridesResponse
{
    [JsonPropertyName("entity_id")] public Guid EntityId { get; init; }
    [JsonPropertyName("fields_updated")] public int FieldsUpdated { get; init; }
    [JsonPropertyName("updated_keys")] public IReadOnlyList<string> UpdatedKeys { get; init; } = [];
    [JsonPropertyName("display_overrides")]
    public Dictionary<string, string> DisplayOverrides { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    [JsonPropertyName("message")] public string Message { get; init; } = "";
}

public sealed class ItemEditorPreferencesRequest
{
    [JsonPropertyName("expected_revision")] public long ExpectedRevision { get; init; }
    [JsonPropertyName("display_overrides")]
    public Dictionary<string, string> DisplayOverrides { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    [JsonPropertyName("personal_notes")] public string? PersonalNotes { get; init; }
    [JsonPropertyName("local_tags")] public List<string> LocalTags { get; init; } = [];
    [JsonPropertyName("is_hidden")] public bool IsHidden { get; init; }
    [JsonPropertyName("include_in_recommendations")] public bool IncludeInRecommendations { get; init; } = true;
}

public sealed class ItemEditorPreferencesResponse
{
    [JsonPropertyName("profile_id")] public Guid ProfileId { get; init; }
    [JsonPropertyName("work_id")] public Guid WorkId { get; init; }
    [JsonPropertyName("personal_notes")] public string? PersonalNotes { get; init; }
    [JsonPropertyName("local_tags")] public IReadOnlyList<string> LocalTags { get; init; } = [];
    [JsonPropertyName("is_hidden")] public bool IsHidden { get; init; }
    [JsonPropertyName("include_in_recommendations")] public bool IncludeInRecommendations { get; init; } = true;
    [JsonPropertyName("revision")] public long Revision { get; init; }
    [JsonPropertyName("updated_at")] public DateTimeOffset? UpdatedAt { get; init; }
    [JsonPropertyName("display_overrides")]
    public IReadOnlyDictionary<string, string> DisplayOverrides { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
