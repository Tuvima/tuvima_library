using MediaEngine.Domain.Models;

namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Orchestrates user-triggered metadata search across Wikidata (Universe mode)
/// and retail providers (Retail mode).
///
/// Universe search: calls WikidataAdapter.ResolveCandidatesAsync, then chains to
/// retail providers to fetch cover art and Wikipedia description for the top candidates.
///
/// Retail search: calls the relevant retail providers (filtered by media type) to
/// return title/cover matches without requiring a Wikidata QID.
/// </summary>
public interface ISearchService
{
    /// <summary>
    /// Search Wikidata for identity candidates, enriching the top results with
    /// cover art (from retail providers) and Wikipedia description.
    /// </summary>
    Task<SearchUniverseResult> SearchUniverseAsync(
        SearchUniverseRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Search retail providers (filtered by media type) for cover art and basic metadata.
    /// Used when no Wikidata entry exists yet (new releases, obscure items).
    /// </summary>
    Task<SearchRetailResult> SearchRetailAsync(
        SearchRetailRequest request,
        CancellationToken ct = default);
}
