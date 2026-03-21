namespace MediaEngine.Domain.Entities;

/// <summary>
/// Represents a batch of files processed together during a single ingestion run.
///
/// Aggregates per-file outcomes (registered, needs review, no match, failed) so
/// the Dashboard can display a single progress card for an import session rather
/// than one entry per file.
///
/// Status values: running → completed | failed
/// </summary>
public sealed class IngestionBatch
{
    /// <summary>Unique identifier for this ingestion batch.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Current batch status. One of: running, completed, failed.
    /// </summary>
    public string Status { get; set; } = "running";

    /// <summary>Source folder path that triggered this batch (nullable — may be absent for ad-hoc runs).</summary>
    public string? SourcePath { get; set; }

    /// <summary>Media category for this batch, e.g. "Books", "Movies" (nullable — may span multiple categories).</summary>
    public string? Category { get; set; }

    /// <summary>Total number of files queued in this batch.</summary>
    public int FilesTotal { get; set; } = 0;

    /// <summary>Number of files that have reached a terminal state (registered, review, no match, or failed).</summary>
    public int FilesProcessed { get; set; } = 0;

    /// <summary>Number of files that were successfully auto-matched and registered in the library.</summary>
    public int FilesRegistered { get; set; } = 0;

    /// <summary>Number of files placed in the review queue due to low confidence or ambiguous matches.</summary>
    public int FilesReview { get; set; } = 0;

    /// <summary>Number of files for which no metadata match could be found.</summary>
    public int FilesNoMatch { get; set; } = 0;

    /// <summary>Number of files that encountered a processing error.</summary>
    public int FilesFailed { get; set; } = 0;

    /// <summary>When batch processing began.</summary>
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>When batch processing finished; null while the batch is still running.</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>When this record was first created.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>When this record was last updated.</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
