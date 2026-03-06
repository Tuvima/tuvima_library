namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Scans the library for orphaned database records whose backing files no
/// longer exist on disk, and cleans up the associated data and filesystem
/// artifacts.
///
/// Implemented in the Api layer by <c>LibraryReconciliationService</c>.
/// Consumed by the ingestion engine at startup to ensure a clean state
/// before scanning the watch folder for new files.
/// </summary>
public interface IReconciliationService
{
    /// <summary>
    /// Runs a full reconciliation scan and returns the result summary.
    /// </summary>
    Task<ReconciliationSummary> ReconcileAsync(CancellationToken ct = default);
}

/// <summary>
/// Summarises the outcome of a reconciliation scan.
/// </summary>
public sealed record ReconciliationSummary(int TotalScanned, int MissingCount, long ElapsedMs);
