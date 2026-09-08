namespace MediaEngine.Storage;

/// <summary>
/// Shared durable activity rules for queries using ingestion_batches alias b.
/// A previous process's batch status cannot hide work that workers can resume.
/// Review states are terminal for ingestion and belong in the review queue.
/// </summary>
public static class IngestionBatchActivitySql
{
    public const string HasOutstandingWork = """
        (EXISTS (SELECT 1 FROM media_operations live
            WHERE live.batch_id = b.id AND live.status IN
                ('pending','queued','leased','running','processing','active','retry_waiting','failed_retryable','interrupted'))
         OR EXISTS (SELECT 1 FROM identity_jobs live
            WHERE live.ingestion_run_id = b.id AND live.state IN
                ('Queued','RetailSearching','RetailMatched','BridgeSearching','QidResolved','Hydrating','UniverseEnriching')))
        """;

    public const string IsActive = "(LOWER(b.status) IN ('running','processing','active','queued') OR "
        + HasOutstandingWork + ")";
}
