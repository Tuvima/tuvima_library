using System.Text.Json.Serialization;

namespace MediaEngine.Contracts.Metadata;

/// <summary>One title-search row embedded in the Wikidata diagnostic response.</summary>
public sealed record WikidataSearchItemResponse(
    string? id,
    string? label,
    string? description);

/// <summary>
/// Responses promoted from anonymous types previously returned directly by
/// <c>src/MediaEngine.Api/Endpoints/MetadataEndpoints.cs</c> artwork upload/lookup
/// endpoints. Property names are deliberately left byte-identical (same casing,
/// no <c>[JsonPropertyName]</c>) to the anonymous object member names they
/// replace, so the wire shape does not change even though this project's
/// serializer applies camelCase naming.
///
/// <para>
/// Note: this intentionally does not follow the snake_case
/// <c>[JsonPropertyName]</c> convention used by the pre-existing endpoint-local
/// records in <c>MetadataEndpoints.cs</c> (e.g. <c>ArtworkEditorEnvelope</c>,
/// <c>ArtworkVariantEnvelope</c>) — that convention was not applied here per the
/// wire-compatibility requirement for this conversion. See
/// <c>MediaEngine.Contracts.Settings.ProviderHealthStatusResponse</c> for the
/// precedent this follows.
/// </para>
/// </summary>
public sealed record ArtworkUploadResponse(
    Guid entity_id,
    string asset_type,
    Guid variant_id,
    string? image_url);

/// <summary>
/// Response promoted from the anonymous type previously returned by the
/// scope-aware artwork upload endpoints (<c>POST /metadata/{entityId}/artwork/{scopeId}/{assetType}</c>
/// and the <c>/from-url</c> variant). See <see cref="ArtworkUploadResponse"/>
/// for the wire-compatibility note this follows.
/// </summary>
public sealed record ScopedArtworkUploadResponse(
    Guid entity_id,
    string scope_id,
    Guid? owner_entity_id,
    string asset_type,
    Guid variant_id,
    string? image_url);

/// <summary>
/// Response promoted from the anonymous type previously returned by
/// <c>PUT /metadata/artwork/{variantId}/preferred</c>. See
/// <see cref="ArtworkUploadResponse"/> for the wire-compatibility note this follows.
/// </summary>
public sealed record ArtworkVariantPreferredResponse(
    Guid variant_id,
    string asset_type,
    string? image_url);

/// <summary>
/// Response promoted from the anonymous type previously returned by
/// <c>DELETE /metadata/artwork/{variantId}</c>. See
/// <see cref="ArtworkUploadResponse"/> for the wire-compatibility note this follows.
/// </summary>
public sealed record ArtworkVariantDeletedResponse(
    Guid variant_id,
    string asset_type,
    Guid? preferred_variant_id);

/// <summary>
/// Response promoted from the anonymous <c>new { results_json = json }</c>
/// previously returned by <c>GET /metadata/{entityId}/search-cache</c>. See
/// <see cref="ArtworkUploadResponse"/> for the wire-compatibility note this follows.
/// </summary>
public sealed record SearchResultsCacheResponse(string results_json);

/// <summary>
/// Response promoted from the anonymous type previously returned by
/// <c>POST /metadata/{entityId}/cover-from-url</c>. See
/// <see cref="ArtworkUploadResponse"/> for the wire-compatibility note this follows.
/// </summary>
public sealed record CoverFromUrlResponse(
    Guid entity_id,
    Guid variant_id,
    string cover_path,
    string? primary_hex,
    string? secondary_hex,
    string? accent_hex);

/// <summary>
/// Response promoted from the anonymous <c>new { qid, label, aliases }</c> shape
/// returned from all four exit paths of <c>GET /metadata/{qid}/aliases</c>. See
/// <see cref="ArtworkUploadResponse"/> for the wire-compatibility note this follows.
/// </summary>
public sealed record WikidataAliasesResponse(
    [property: JsonPropertyName("qid")] string Qid,
    [property: JsonPropertyName("label")] string? Label,
    [property: JsonPropertyName("aliases")] IReadOnlyList<string> Aliases);
