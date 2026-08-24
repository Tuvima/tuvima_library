using System.Text.Json.Serialization;

namespace MediaEngine.Contracts.LocalAssets;

/// <summary>
/// Truthful availability state for a View discovery surface. Automatic processing is
/// reported separately from indexed evidence so stored user/reviewed data never implies
/// that an AI processor is installed or running.
/// </summary>
public sealed record ViewDiscoveryCapabilityDto(
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("has_indexed_data")] bool HasIndexedData,
    [property: JsonPropertyName("automatic_processing_available")] bool AutomaticProcessingAvailable,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("evidence_kinds")] IReadOnlyList<string> EvidenceKinds);

public sealed record ViewPlaceDto(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("latitude")] double Latitude,
    [property: JsonPropertyName("longitude")] double Longitude,
    [property: JsonPropertyName("asset_count")] int AssetCount,
    [property: JsonPropertyName("representative_library_id")] Guid RepresentativeLibraryId,
    [property: JsonPropertyName("representative_asset_id")] Guid RepresentativeAssetId);

public sealed record ViewPlacesPageDto(
    [property: JsonPropertyName("items")] IReadOnlyList<ViewPlaceDto> Items,
    [property: JsonPropertyName("next_cursor")] string? NextCursor,
    [property: JsonPropertyName("has_more")] bool HasMore,
    [property: JsonPropertyName("capability")] ViewDiscoveryCapabilityDto Capability);

public sealed record ViewPersonDto(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("asset_count")] int AssetCount,
    [property: JsonPropertyName("representative_library_id")] Guid RepresentativeLibraryId,
    [property: JsonPropertyName("representative_asset_id")] Guid RepresentativeAssetId,
    [property: JsonPropertyName("annotation_kinds")] IReadOnlyList<string> AnnotationKinds,
    [property: JsonPropertyName("provenance_sources")] IReadOnlyList<string> ProvenanceSources,
    [property: JsonPropertyName("has_reviewed_evidence")] bool HasReviewedEvidence);

public sealed record ViewPeoplePageDto(
    [property: JsonPropertyName("items")] IReadOnlyList<ViewPersonDto> Items,
    [property: JsonPropertyName("next_cursor")] string? NextCursor,
    [property: JsonPropertyName("has_more")] bool HasMore,
    [property: JsonPropertyName("capability")] ViewDiscoveryCapabilityDto Capability);

public static class ViewDiscoveryCapabilityStates
{
    public const string Available = "available";
    public const string Empty = "empty";
    public const string NoMatches = "no_matches";
}
