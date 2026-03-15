using MediaEngine.Domain.Models;

namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Detects discrepancies between an edition's metadata and its master work's
/// canonical values from Wikidata (P629 — edition_or_translation_of).
/// </summary>
public interface ICanonDiscrepancyService
{
    /// <summary>
    /// Compare canonical values of the given entity against its master work.
    /// Returns an empty list if the entity is not an edition or has no master work.
    /// </summary>
    Task<IReadOnlyList<CanonDiscrepancy>> DetectAsync(
        Guid entityId, CancellationToken ct = default);
}
