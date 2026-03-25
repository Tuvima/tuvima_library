namespace MediaEngine.Domain.Contracts;

using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Models;

/// <summary>
/// Extracts person signals from description text and file metadata using
/// config-driven regex patterns. Runs inline during hydration with zero
/// API calls — deposits unverified claims and records pending signals
/// for later batch verification.
/// </summary>
public interface IDescriptionSignalExtractor
{
    /// <summary>
    /// Extract person names from the description and file metadata,
    /// deposit unverified claims, and record in pending_person_signals.
    /// No Wikidata API calls are made.
    /// </summary>
    Task<IReadOnlyList<ExtractedPersonSignal>> ExtractAndDepositAsync(
        Guid entityId,
        string mediaType,
        IReadOnlyList<MetadataClaim> rawClaims,
        IReadOnlyList<CanonicalValue> canonicals,
        IReadOnlyDictionary<string, string>? fileHints = null,
        CancellationToken ct = default);
}
