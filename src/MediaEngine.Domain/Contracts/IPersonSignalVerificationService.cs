namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Batch-verifies pending person signals against Wikidata after an
/// ingestion batch completes. Deduplicates names, searches Wikidata,
/// validates P31 (human) + P106 (occupation), and creates Person records
/// for verified signals.
/// </summary>
public interface IPersonSignalVerificationService
{
    /// <summary>
    /// Process all pending person signals: deduplicate, verify against
    /// Wikidata in batch, upgrade claims for verified persons, create
    /// Person records, and clean up processed signals.
    /// </summary>
    /// <returns>Number of persons verified and created.</returns>
    Task<int> VerifyPendingSignalsAsync(CancellationToken ct = default);
}
