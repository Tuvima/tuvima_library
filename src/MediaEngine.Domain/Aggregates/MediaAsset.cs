using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;

namespace MediaEngine.Domain.Aggregates;

/// <summary>
/// A single physical media file (or a group of files unified by a
/// <see cref="MediaManifest"/>) as it exists on the local file system.
///
/// <see cref="ContentHash"/> is the primary identity key across moves:
/// user progress MUST be preserved when a file is relocated,
/// provided the hash is unchanged.
/// Spec: Phase 2 – Invariants § Asset Integrity and Progress Persistence.
///
/// Maps to <c>media_assets</c> in the Phase 4 schema.
/// </summary>
public sealed class MediaAsset
{
    /// <summary>Stable identifier. PK in <c>media_assets</c>.</summary>
    public Guid Id { get; set; }

    /// <summary>
    /// FK → <c>editions.id</c>.
    /// Spec invariant: "A MediaAsset MUST have a verified hash before it is
    /// linked to an Edition."  Verification happens at the application layer.
    /// </summary>
    public Guid EditionId { get; set; }

    /// <summary>
    /// Content-addressable hash (e.g. SHA-256) of the media file.
    /// UNIQUE in the database.  The reconciliation anchor for <see cref="UserState"/>.
    /// Spec: Phase 4 – Hash Dominance invariant.
    /// </summary>
    public string ContentHash { get; set; } = string.Empty;

    /// <summary>
    /// Root path on the local file system where this asset resides.
    /// Full binary data is NEVER stored in the database.
    /// Spec: Phase 4 – Binary Storage constraint.
    /// </summary>
    public string FilePathRoot { get; set; } = string.Empty;

    /// <summary>
    /// Current lifecycle status of this asset.
    /// Assets in <see cref="AssetStatus.Conflicted"/> state MUST NOT be
    /// automatically assigned to a Hub.
    /// </summary>
    public AssetStatus Status { get; set; } = AssetStatus.Normal;

    /// <summary>
    /// Present when this asset consists of more than one physical file
    /// (e.g. multi-disc movie, split audiobook).
    /// Null for single-file assets.
    /// Spec: "MUST be treated as a single MediaAsset via a MediaManifest."
    /// </summary>
    public MediaManifest? Manifest { get; set; }
}
