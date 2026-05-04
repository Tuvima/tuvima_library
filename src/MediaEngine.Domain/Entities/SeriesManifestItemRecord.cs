namespace MediaEngine.Domain.Entities;

/// <summary>
/// Local persisted row for a factual Wikidata series manifest item.
/// </summary>
public sealed class SeriesManifestItemRecord
{
    public Guid Id { get; init; }
    public Guid CollectionId { get; init; }
    public required string SeriesQid { get; init; }
    public required string ItemQid { get; init; }
    public string? ItemLabel { get; init; }
    public string? ItemDescription { get; init; }
    public string? MediaType { get; init; }
    public string? RawOrdinal { get; init; }
    public double? ParsedOrdinal { get; init; }
    public double? SortOrder { get; init; }
    public string? PublicationDate { get; init; }
    public string? PreviousQid { get; init; }
    public string? NextQid { get; init; }
    public string? ParentCollectionQid { get; init; }
    public string? ParentCollectionLabel { get; init; }
    public bool IsCollection { get; init; }
    public bool IsExpandedFromCollection { get; init; }
    public string SourcePropertiesJson { get; init; } = "[]";
    public string RelationshipsJson { get; init; } = "[]";
    public required string OrderSource { get; init; }
    public string OwnershipState { get; init; } = "Missing";
    public Guid? LinkedWorkId { get; init; }
    public DateTimeOffset LastHydratedAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}
