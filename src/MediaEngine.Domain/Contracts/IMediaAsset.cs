using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;

namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Defines the interface for interacting with file-system resources.
/// Implemented by <see cref="Aggregates.MediaAsset"/>.
/// Spec: Phase 2 – Interfaces § IMediaAsset.
/// </summary>
public interface IMediaAsset
{
    Guid Id { get; }
    Guid EditionId { get; }

    /// <summary>
    /// Content-addressable hash.  The reconciliation anchor for
    /// <see cref="UserState"/> across file moves.
    /// </summary>
    string ContentHash { get; }

    /// <summary>Root path on the local file system. No BLOBs in the database.</summary>
    string FilePathRoot { get; }

    AssetStatus Status { get; }

    /// <summary>Non-null for multi-file assets (audiobooks, multi-disc films).</summary>
    MediaManifest? Manifest { get; }
}
