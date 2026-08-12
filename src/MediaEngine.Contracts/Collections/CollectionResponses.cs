namespace MediaEngine.Contracts.Collections;

/// <summary>
/// Wire responses for <c>CollectionEndpoints</c> routes that previously returned anonymous
/// types (<c>Results.Ok(new { ... })</c>). Property names are deliberately left exactly as the
/// anonymous types declared them — camelCase or snake_case, not PascalCase — and carry no
/// <c>[JsonPropertyName]</c> overrides, so the JSON payload these records produce is
/// byte-identical to what the replaced anonymous types produced.
/// </summary>
public sealed record CollectionParentResponse(ParentCollectionSummary? parentCollection);

public sealed record ParentCollectionSummary(
    Guid id,
    string? displayName,
    DateTimeOffset createdAt,
    string universeStatus);

public sealed record CollectionChildSummary(
    Guid id,
    string? displayName,
    Guid? parentCollectionId,
    DateTimeOffset createdAt,
    string universeStatus);

public sealed record CollectionLocationPlacementSummary(
    Guid collection_id,
    string name,
    string collection_type,
    string? icon_name,
    string location,
    int position,
    int display_limit,
    string display_mode);

public sealed record CollectionPlacementSummary(
    Guid id,
    string location,
    int position,
    int display_limit,
    string display_mode,
    bool is_visible);

public sealed record CollectionCoverArtworkUploadResponse(string cover_artwork_url);

public sealed record CollectionCreatedResponse(Guid id, string? name);
