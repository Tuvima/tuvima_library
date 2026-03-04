namespace MediaEngine.Ingestion.Models;

/// <summary>
/// Lifecycle stage of a file as it moves through the ingestion pipeline.
/// Spec: Phase 7 – Ingestion Orchestration responsibility.
/// </summary>
public enum IngestionState
{
    /// <summary>File is in a watched directory but has not yet been processed.</summary>
    Watch,

    /// <summary>File is being debounced, hashed, or awaiting metadata resolution.</summary>
    Staging,

    /// <summary>File has been successfully processed and is part of the managed library.</summary>
    Library,

    /// <summary>
    /// File was rejected (unsupported format, corrupt, or duplicate).
    /// Spec invariant: "MUST be moved to a Rejection or Duplicates folder
    /// rather than being deleted."
    /// </summary>
    Rejected,

    /// <summary>
    /// The file-lock probe failed after all retries and the operation
    /// must be retried from the recovery journal.
    /// </summary>
    LockTimeout,
}
