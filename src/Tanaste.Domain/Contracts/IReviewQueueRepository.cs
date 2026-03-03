using Tanaste.Domain.Entities;

namespace Tanaste.Domain.Contracts;

/// <summary>
/// Persistence contract for <see cref="ReviewQueueEntry"/> records.
///
/// The <c>review_queue</c> table stores metadata hydration items that require
/// user intervention — disambiguation, low-confidence confirmation, or manual
/// match fixing.
///
/// Implementations live in <c>Tanaste.Storage</c>.
/// </summary>
public interface IReviewQueueRepository
{
    /// <summary>
    /// Inserts a new review queue entry.
    /// Returns the assigned <see cref="ReviewQueueEntry.Id"/>.
    /// </summary>
    /// <param name="entry">The entry to insert. <see cref="ReviewQueueEntry.Id"/>
    /// should be pre-assigned by the caller.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<Guid> InsertAsync(ReviewQueueEntry entry, CancellationToken ct = default);

    /// <summary>
    /// Returns all pending review items, ordered by creation date (newest first).
    /// </summary>
    /// <param name="limit">Maximum entries to return (default 50).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<ReviewQueueEntry>> GetPendingAsync(
        int limit = 50,
        CancellationToken ct = default);

    /// <summary>
    /// Returns a single review item by its ID, or <c>null</c> if not found.
    /// </summary>
    Task<ReviewQueueEntry?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Returns all review items (any status) for a given entity, ordered newest first.
    /// </summary>
    Task<IReadOnlyList<ReviewQueueEntry>> GetByEntityAsync(
        Guid entityId,
        CancellationToken ct = default);

    /// <summary>
    /// Updates the status of a review item (e.g. Pending → Resolved or Dismissed).
    /// Sets <see cref="ReviewQueueEntry.ResolvedAt"/> to <c>DateTimeOffset.UtcNow</c>
    /// and <see cref="ReviewQueueEntry.ResolvedBy"/> to the given profile identifier.
    /// </summary>
    /// <param name="id">The review item ID.</param>
    /// <param name="status">The new status. Use <see cref="Enums.ReviewStatus"/> constants.</param>
    /// <param name="resolvedBy">The profile that resolved/dismissed this item (nullable).</param>
    /// <param name="ct">Cancellation token.</param>
    Task UpdateStatusAsync(
        Guid id,
        string status,
        string? resolvedBy = null,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the number of pending review items.
    /// Used for sidebar badge count and global notification badge.
    /// </summary>
    Task<int> GetPendingCountAsync(CancellationToken ct = default);
}
