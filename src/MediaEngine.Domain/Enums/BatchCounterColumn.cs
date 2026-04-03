namespace MediaEngine.Domain.Enums;

/// <summary>
/// Names the individual counter columns on <c>ingestion_batches</c> that can be
/// atomically incremented via <c>IIngestionBatchRepository.IncrementCounterAsync</c>.
/// </summary>
public enum BatchCounterColumn
{
    /// <summary>Total number of files seen in this batch (files_total).</summary>
    FilesTotal,

    /// <summary>Files that were fully processed through the ingestion pipeline (files_processed).</summary>
    FilesProcessed,

    /// <summary>Files that were successfully identified and matched (files_registered).</summary>
    FilesIdentified,

    /// <summary>Files routed to the review queue (files_review).</summary>
    FilesReview,

    /// <summary>Files for which no retail or Wikidata match was found (files_no_match).</summary>
    FilesNoMatch,

    /// <summary>Files that failed due to corruption or an unrecoverable error (files_failed).</summary>
    FilesFailed,
}
