using Tanaste.Domain.Enums;
using Tanaste.Providers.Models;
using Tanaste.Storage.Models;

namespace Tanaste.Providers.Contracts;

/// <summary>
/// Contract for a single external metadata adapter.
///
/// Each adapter encapsulates all knowledge about one API: its HTTP shape,
/// throttling requirements, claim mapping, and graceful-failure behaviour.
///
/// A new provider is one class implementing this interface plus one JSON entry
/// in <c>tanaste_master.json</c>; the harvesting engine picks it up automatically.
///
/// Adapters MUST:
/// - Return an empty list on any network failure (not throw).
/// - Respect the throttle limits imposed by the provider's terms of service.
/// - Never hard-code base URLs; read them from <see cref="ProviderLookupRequest.BaseUrl"/>.
///
/// Spec: Phase 9 – External Metadata Adapters § Adapter Contract.
/// </summary>
public interface IExternalMetadataProvider
{
    /// <summary>
    /// Human-readable adapter name.  Matches the <c>name</c> field in
    /// <c>tanaste_master.json</c> providers array.
    /// Examples: <c>"apple_books_ebook"</c>, <c>"audnexus"</c>.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// The media domain this adapter specialises in (Ebook, Audiobook, Universal, …).
    /// Used for UI grouping; does not restrict which requests are dispatched.
    /// </summary>
    ProviderDomain Domain { get; }

    /// <summary>
    /// Declarative list of metadata fields this adapter is expert in.
    /// Examples: <c>["cover", "narrator", "series"]</c>.
    /// Informational only — actual trust is encoded in <c>tanaste_master.json</c>
    /// <c>field_weights</c>.
    /// </summary>
    IReadOnlyList<string> CapabilityTags { get; }

    /// <summary>
    /// Stable GUID that identifies this provider in the <c>provider_registry</c>
    /// and on every <c>metadata_claims</c> row this adapter produces.
    /// Must not change between versions.
    /// </summary>
    Guid ProviderId { get; }

    /// <summary>
    /// Returns <c>true</c> if this adapter can handle the given media type.
    /// Called by the harvesting service to filter candidates before dispatching.
    /// </summary>
    bool CanHandle(MediaType mediaType);

    /// <summary>
    /// Returns <c>true</c> if this adapter can handle the given entity type.
    /// Called by the harvesting service alongside <see cref="CanHandle(MediaType)"/>.
    /// </summary>
    bool CanHandle(EntityType entityType);

    /// <summary>
    /// Fetches metadata claims from the external provider for the given request.
    ///
    /// Implementations MUST catch all network exceptions and return an empty list
    /// rather than propagating failures — the caller continues to the next adapter.
    ///
    /// The returned claims are not yet persisted; the harvesting service converts
    /// them to <see cref="Domain.Entities.MetadataClaim"/> rows and writes them
    /// to the database.
    /// </summary>
    /// <param name="request">Contextual hints for the lookup.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Zero or more claims.  Never <c>null</c>.</returns>
    Task<IReadOnlyList<ProviderClaim>> FetchAsync(
        ProviderLookupRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Searches the external provider and returns multiple result candidates for
    /// user selection (e.g. Needs Review resolution panel).
    ///
    /// Unlike <see cref="FetchAsync"/> which returns the single best match as claims,
    /// this method returns up to <paramref name="limit"/> result items, each carrying
    /// a title, description, thumbnail, and provider-specific item ID for subsequent
    /// direct lookup.
    ///
    /// Default implementation returns an empty list — providers that do not support
    /// multi-result search do not need to override this.
    /// </summary>
    /// <param name="request">Contextual hints for the search.</param>
    /// <param name="limit">Maximum number of results to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Zero or more search result items.  Never <c>null</c>.</returns>
    Task<IReadOnlyList<SearchResultItem>> SearchAsync(
        ProviderLookupRequest request,
        int limit = 25,
        CancellationToken ct = default)
    {
        // Default: no multi-result support. Override in adapters that support it.
        return Task.FromResult<IReadOnlyList<SearchResultItem>>([]);
    }
}
