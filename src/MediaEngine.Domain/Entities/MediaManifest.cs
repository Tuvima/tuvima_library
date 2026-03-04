namespace MediaEngine.Domain.Entities;

/// <summary>
/// Describes a multi-file <see cref="Aggregates.MediaAsset"/>.
///
/// A single logical asset (e.g. a 3-part audiobook) is represented as one
/// <c>MediaAsset</c> plus one <c>MediaManifest</c> that lists each constituent file.
/// This ensures progress tracking remains continuous across file boundaries.
///
/// Spec: "Audiobooks or multi-disc movies MUST be treated as a single MediaAsset
/// via a MediaManifest to ensure continuous tracking."
/// Extension Point: Phase 2 – Media Extension Modules.
///
/// Note: This entity has no corresponding table in the Phase 4 schema; storage
/// support will be added in a future schema revision.
/// </summary>
public sealed class MediaManifest
{
    /// <summary>Stable identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The <see cref="Aggregates.MediaAsset"/> this manifest belongs to.
    /// FK → <c>media_assets.id</c>.
    /// </summary>
    public Guid MediaAssetId { get; set; }

    /// <summary>
    /// Ordered list of constituent files.
    /// MUST be sorted by <see cref="MediaManifestEntry.SortIndex"/> before use.
    /// </summary>
    public List<MediaManifestEntry> Entries { get; set; } = [];
}
