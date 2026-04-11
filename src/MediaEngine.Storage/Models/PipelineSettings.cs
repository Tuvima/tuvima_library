using System.Text.Json.Serialization;

namespace MediaEngine.Storage.Models;

/// <summary>
/// Identity pipeline tuning parameters loaded from <c>config/core.json</c>
/// (<c>pipeline</c> section).
///
/// Centralises lease batch sizes for the three identity workers so they can
/// be tuned without code changes — and so the cross-file batching policy is
/// authored in one place rather than scattered as <c>const int BatchSize</c>
/// across each worker.
/// </summary>
public sealed class PipelineSettings
{
    /// <summary>Lease batch sizes for identity worker polling cycles.</summary>
    [JsonPropertyName("lease_sizes")]
    public LeaseSizeSettings LeaseSizes { get; set; } = new();

    /// <summary>Batch gate settings — holds Stage 2 until Stage 1 drains for a run.</summary>
    [JsonPropertyName("batch_gate")]
    public BatchGateSettings BatchGate { get; set; } = new();
}

/// <summary>
/// Controls the batch gate that holds Stage 2 (Wikidata bridge resolution)
/// until all Stage 1 (retail match) jobs for a given ingestion run have
/// completed. This ensures a larger, more coherent batch reaches Wikidata
/// so cross-file deduplication (e.g. all tracks from the same album) can
/// operate on the full set rather than whichever files happened to finish
/// retail matching first.
/// </summary>
public sealed class BatchGateSettings
{
    /// <summary>Gate is active. When false, Stage 2 processes immediately.</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>Batches with this many files or fewer skip the gate entirely.</summary>
    [JsonPropertyName("small_batch_threshold")]
    public int SmallBatchThreshold { get; set; } = 5;

    /// <summary>Maximum seconds to hold a batch before force-releasing.</summary>
    [JsonPropertyName("timeout_seconds")]
    public int TimeoutSeconds { get; set; } = 300;
}

/// <summary>
/// Per-worker lease batch sizes. A worker leases up to this many jobs in a
/// single poll cycle, so larger values group more files into one batch
/// (one Apple album call covers more tracks, one TMDB season call covers
/// more episodes, one Wikidata reconciliation call covers more bridge IDs).
///
/// Defaults are tuned for "drop a folder of 50 files" being a single
/// efficient batch rather than five back-to-back leases of 10.
/// </summary>
public sealed class LeaseSizeSettings
{
    /// <summary>
    /// Stage 1 retail matching lease size. Default 50 — large enough that
    /// a typical TV season or music album drop processes in a single lease.
    /// </summary>
    [JsonPropertyName("retail")]
    public int Retail { get; set; } = 50;

    /// <summary>
    /// Stage 2 Wikidata bridge resolution lease size. Default 50 — matches
    /// retail so the two stages stay in lockstep when a large batch lands.
    /// </summary>
    [JsonPropertyName("wikidata")]
    public int Wikidata { get; set; } = 50;

    /// <summary>
    /// Quick hydration lease size. Default 20 — hydration is per-job
    /// (no cross-job grouping benefit) so the limit is set lower to keep
    /// individual lease cycles responsive.
    /// </summary>
    [JsonPropertyName("hydration")]
    public int Hydration { get; set; } = 20;
}
