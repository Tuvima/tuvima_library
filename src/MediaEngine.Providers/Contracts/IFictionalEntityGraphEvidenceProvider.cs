using MediaEngine.Providers.Models;

namespace MediaEngine.Providers.Contracts;

/// <summary>
/// Optional provider capability for Stage 3 graph enrichment. It deliberately
/// keeps raw statement qualifiers separate from the flattened metadata-claim
/// contract used by normal scoring.
/// </summary>
public interface IFictionalEntityGraphEvidenceProvider
{
    /// <summary>Fetches configured entity-valued statements and their qualifiers.</summary>
    Task<FictionalEntityGraphEvidence?> FetchFictionalEntityGraphEvidenceAsync(
        string qid,
        string entitySubType,
        CancellationToken ct = default);
}
