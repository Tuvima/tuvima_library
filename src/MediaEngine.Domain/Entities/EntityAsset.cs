namespace MediaEngine.Domain.Entities;

/// <summary>
/// A typed image asset belonging to any entity in the library
/// (Work, Person, Universe, or FictionalEntity).
///
/// The <see cref="AssetTypeValue"/> classifies the image (CoverArt, Headshot,
/// Banner, SquareArt, Logo, Background). Multiple assets of the same type may exist per entity
/// (e.g. three different movie posters); one is marked as preferred.
///
/// Maps 1:1 to a row in the <c>entity_assets</c> table.
/// </summary>
public sealed class EntityAsset
{
    /// <summary>Stable row identifier (UUID → TEXT in SQLite).</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The entity this asset belongs to (work ID, person ID, universe QID, or entity ID).
    /// Stored as TEXT to support both GUIDs and Wikidata QIDs.
    /// </summary>
    public string EntityId { get; set; } = string.Empty;

    /// <summary>
    /// The type of entity: <c>"Work"</c>, <c>"Person"</c>, <c>"Universe"</c>,
    /// or <c>"FictionalEntity"</c>.
    /// </summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>
    /// The asset classification. One of: <c>"CoverArt"</c>, <c>"Headshot"</c>,
    /// <c>"Banner"</c>, <c>"SquareArt"</c>, <c>"Logo"</c>, <c>"Background"</c>.
    /// </summary>
    public string AssetTypeValue { get; set; } = string.Empty;

    /// <summary>Remote URL of the image (e.g. Fanart.tv CDN, Wikimedia Commons).</summary>
    public string? ImageUrl { get; set; }

    /// <summary>Local filesystem path to the downloaded image.</summary>
    public string? LocalImagePath { get; set; }

    /// <summary>
    /// Which provider supplied this asset.
    /// Example: <c>"fanart_tv"</c>, <c>"wikidata"</c>, <c>"tmdb"</c>, <c>"user_upload"</c>.
    /// </summary>
    public string? SourceProvider { get; set; }

    /// <summary>Broad classification for the asset record.</summary>
    public string AssetClassValue { get; set; } = "Artwork";

    /// <summary>Current storage location for the managed file.</summary>
    public string StorageLocationValue { get; set; } = "Central";

    /// <summary>Human-readable ownership scope such as Series, Season, Album, or Work.</summary>
    public string OwnerScope { get; set; } = "Unknown";

    /// <summary>Whether this is the preferred asset for its entity + type combination.</summary>
    public bool IsPreferred { get; set; }

    /// <summary>Whether the user explicitly chose this asset (overrides auto-selection).</summary>
    public bool IsUserOverride { get; set; }

    /// <summary>Whether the preferred asset has been mirrored to a local export path.</summary>
    public bool IsLocallyExported { get; set; }

    /// <summary>Whether this variant is the currently exported preferred mirror.</summary>
    public bool IsPreferredExported { get; set; }

    /// <summary>When this asset record was created.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>When this asset was last updated.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }
}
