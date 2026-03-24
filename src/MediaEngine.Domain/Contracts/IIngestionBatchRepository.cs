using MediaEngine.Domain.Entities;

namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Persistence contract for ingestion batch records.
/// Tracks aggregate progress across a set of files processed during a single
/// ingestion run, providing a single-row answer to "how did this import go?"
/// </summary>
public interface IIngestionBatchRepository
{
    /// <summary>
    /// Inserts a new ingestion batch record when a run begins.
    /// </summary>
    Task CreateAsync(IngestionBatch batch, CancellationToken ct = default);

    /// <summary>
    /// Updates the counter columns for an in-progress batch.
    /// Called incrementally as files reach terminal states.
    /// </summary>
    Task UpdateCountsAsync(
        Guid id,
        int filesTotal,
        int filesProcessed,
        int filesIdentified,
        int filesReview,
        int filesNoMatch,
        int filesFailed,
        CancellationToken ct = default);

    /// <summary>
    /// Marks the batch as finished by setting <c>CompletedAt</c> to the current UTC time
    /// and updating the status to either "completed" or "failed".
    /// </summary>
    Task CompleteAsync(Guid id, string status, CancellationToken ct = default);

    /// <summary>
    /// Returns a single ingestion batch by its unique identifier, or <c>null</c> if not found.
    /// </summary>
    Task<IngestionBatch?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Returns the most recent ingestion batches, ordered newest-first.
    /// </summary>
    Task<IReadOnlyList<IngestionBatch>> GetRecentAsync(int limit = 20, CancellationToken ct = default);

    /// <summary>
    /// Returns the total number of files across all batches that require user attention
    /// (i.e. the sum of <c>FilesReview</c> and <c>FilesNoMatch</c> across all batches
    /// where at least one such file exists).
    /// </summary>
    Task<int> GetNeedsAttentionCountAsync(CancellationToken ct = default);

    /// <summary>
    /// Marks all batches currently in "running" status as "abandoned".
    /// Called on Engine startup to clean up batches that were interrupted
    /// by a previous shutdown. Returns the number of batches abandoned.
    /// </summary>
    Task<int> AbandonRunningAsync(CancellationToken ct = default);
}
