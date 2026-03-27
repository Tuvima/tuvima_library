using MediaEngine.Domain.Models;

namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Searches Wikidata for person entities by name and role.
/// Used to resolve unlinked person names (e.g. narrators from file metadata)
/// to Wikidata QIDs when structured properties (P50, P175, etc.) are unavailable.
/// </summary>
public interface IPersonReconciliationService
{
    /// <summary>
    /// Searches Wikidata for a person matching the given name and expected role.
    /// Returns a result only if confidence meets the auto-accept threshold (0.80).
    /// Returns <c>null</c> if no confident match is found (auto-skip for 30-day retry).
    /// </summary>
    /// <param name="name">The person's name to search for.</param>
    /// <param name="expectedRole">Expected role: Author, Narrator, Director, Screenwriter, Composer, Cast Member, Illustrator.</param>
    /// <param name="workTitle">Optional work title for notable-work matching boost.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<PersonSearchResult?> SearchPersonAsync(
        string name,
        string expectedRole,
        string? workTitle = null,
        CancellationToken ct = default);
}
