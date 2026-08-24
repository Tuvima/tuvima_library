using System.Text.Json;
using System.Text.Json.Serialization;

namespace MediaEngine.Contracts.Collections;

public static class CollectionPersonalMediaSourceKinds
{
    public const string Gallery = "gallery";
    public const string SmartRule = "smart_rule";
}

/// <summary>
/// A Gallery that the trusted active profile may attach to a Custom Collection.
/// Item identifiers and membership counts are intentionally absent.
/// </summary>
public sealed record CollectionGalleryReferenceDto(
    [property: JsonPropertyName("gallery_id")] Guid GalleryId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("gallery_kind")] string GalleryKind,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt);

/// <summary>
/// Count-free personal-media source metadata. A Collection stores either one
/// Gallery reference or one versioned View rule, never expanded LocalAsset IDs.
/// </summary>
public sealed record CollectionPersonalMediaSourceDto(
    [property: JsonPropertyName("source_id")] Guid SourceId,
    [property: JsonPropertyName("collection_id")] Guid CollectionId,
    [property: JsonPropertyName("owner_profile_id")] Guid OwnerProfileId,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("gallery_id")] Guid? GalleryId,
    [property: JsonPropertyName("rule_version")] int? RuleVersion,
    [property: JsonPropertyName("rule_definition")] CollectionRuleDefinitionDto? RuleDefinition,
    [property: JsonPropertyName("position")] int Position);

/// <summary>
/// Admin write contract for a personal-media source. Unknown request members
/// are captured so the API can explicitly reject attempts to smuggle individual
/// asset IDs into Collection membership.
/// </summary>
public sealed class CollectionPersonalMediaSourceWriteRequest
{
    [JsonPropertyName("kind")] public string Kind { get; init; } = string.Empty;
    [JsonPropertyName("gallery_id")] public Guid? GalleryId { get; init; }
    [JsonPropertyName("rule_version")] public int? RuleVersion { get; init; }
    [JsonPropertyName("rule_definition")] public CollectionRuleDefinitionDto? RuleDefinition { get; init; }
    [JsonPropertyName("position")] public int Position { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalMembers { get; init; }
}
