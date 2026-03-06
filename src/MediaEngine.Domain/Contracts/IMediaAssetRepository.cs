using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Enums;

namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Defines the persistence contract for <see cref="MediaAsset"/> records.
/// Implementations live in <c>MediaEngine.Storage</c>.
///
/// This interface is in the Domain layer so the ingestion engine can depend on it
/// without referencing the storage implementation directly.
///
/// Spec: Phase 4 – Hash Dominance invariant (content_hash UNIQUE);
///       Phase 7 – Asset Integrity; Conflict and Orphan handling.
/// </summary>
public interface IMediaAssetRepository
{
    /// <summary>
    /// Returns the asset whose <c>content_hash</c> matches <paramref name="contentHash"/>,
    /// or <see langword="null"/> if no such asset exists.
    ///
    /// Primary duplicate-detection call: invoke this before <see cref="InsertAsync"/>
    /// to honour the Hash Dominance invariant.
    /// </summary>
    Task<MediaAsset?> FindByHashAsync(string contentHash, CancellationToken ct = default);

    /// <summary>
    /// Returns the asset with <paramref name="id"/>, or <see langword="null"/> if not found.
    /// </summary>
    Task<MediaAsset?> FindByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Inserts <paramref name="asset"/> into <c>media_assets</c>.
    ///
    /// Uses <c>INSERT OR IGNORE</c> on the <c>content_hash</c> unique constraint,
    /// so a concurrent duplicate will not throw — it is silently skipped.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when a new row was written;
    /// <see langword="false"/> when the hash already existed (duplicate file).
    /// </returns>
    Task<bool> InsertAsync(MediaAsset asset, CancellationToken ct = default);

    /// <summary>
    /// Returns the asset whose <c>file_path_root</c> matches <paramref name="pathRoot"/>,
    /// or <see langword="null"/> if no such asset exists.
    ///
    /// Used by the deletion handler to locate the asset record when the file
    /// has already been removed from disk (content hash is unavailable).
    /// Spec: Phase B – Deleted File Cleanup (B-04).
    /// </summary>
    Task<MediaAsset?> FindByPathRootAsync(string pathRoot, CancellationToken ct = default);

    /// <summary>
    /// Updates the <c>status</c> column for the asset identified by <paramref name="id"/>.
    /// Used to transition assets through the
    /// Normal → Conflicted / Normal → Orphaned lifecycle states.
    /// </summary>
    Task UpdateStatusAsync(Guid id, AssetStatus status, CancellationToken ct = default);

    /// <summary>
    /// Updates the <c>file_path_root</c> column for the asset identified by <paramref name="id"/>.
    /// Called after a file is re-organized (moved from the Watch Folder to the Library)
    /// so subsequent lookups reflect the new location.
    /// </summary>
    Task UpdateFilePathAsync(Guid id, string newPath, CancellationToken ct = default);

    /// <summary>
    /// Permanently deletes the asset record identified by <paramref name="id"/>.
    /// Used during orphan cleanup when the file no longer exists on disk.
    /// </summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Returns all assets with the given <paramref name="status"/>.
    /// Used by the reconciliation service to scan for Normal-status assets
    /// and verify their files still exist on disk.
    /// </summary>
    Task<IReadOnlyList<MediaAsset>> ListByStatusAsync(AssetStatus status, CancellationToken ct = default);
}
