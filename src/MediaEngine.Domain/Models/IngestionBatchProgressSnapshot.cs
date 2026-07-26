namespace MediaEngine.Domain.Models;

/// <summary>
/// Read model for the latest identity-job state of an ingestion batch.
/// </summary>
public sealed class IngestionBatchProgressSnapshot
{
    public int TotalJobs { get; init; }
    public int FilesReady { get; init; }
    public int FilesReadyWithoutUniverse { get; init; }
    public int FilesReview { get; init; }
    public int FilesNoMatch { get; init; }
    public int PipelineFailed { get; init; }
    public int QueuedJobs { get; init; }
    public int RetailSearching { get; init; }
    public int RetailMatched { get; init; }
    public int RetailMatchedNeedsReview { get; init; }
    public int BridgeSearching { get; init; }
    public int QidResolved { get; init; }
    public int Hydrating { get; init; }
    public int UniverseEnriching { get; init; }
    public string? CurrentFileTitle { get; init; }
}
