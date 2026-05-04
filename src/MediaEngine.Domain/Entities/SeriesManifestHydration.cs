namespace MediaEngine.Domain.Entities;

/// <summary>
/// Series-level cache/provenance row for a Wikidata series manifest.
/// </summary>
public sealed class SeriesManifestHydration
{
    public required string SeriesQid { get; init; }
    public Guid CollectionId { get; init; }
    public string? SeriesLabel { get; init; }
    public string ManifestSource { get; init; } = "Tuvima.Wikidata";
    public string? ManifestVersion { get; init; }
    public string? ManifestHash { get; init; }
    public string? KnownItemQidsHash { get; init; }
    public string WarningsJson { get; init; } = "[]";
    public string ApiMetadataJson { get; init; } = "{}";
    public DateTimeOffset LastHydratedAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}
