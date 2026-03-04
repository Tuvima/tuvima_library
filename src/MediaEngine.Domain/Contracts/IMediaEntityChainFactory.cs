using MediaEngine.Domain.Enums;

namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Creates or finds the Hub → Work → Edition chain required before a
/// <see cref="Aggregates.MediaAsset"/> can be inserted into the database.
///
/// The <c>media_assets</c> table has a NOT NULL FK to <c>editions(id)</c>,
/// which in turn references <c>works(id)</c> → <c>hubs(id)</c>.  This
/// factory ensures the chain exists and returns the <c>EditionId</c> to use.
///
/// Implementations may reuse existing Hubs when a matching display name is
/// found (case-insensitive).  When no match is found a new Hub → Work →
/// Edition chain is created in a single transaction.
/// </summary>
public interface IMediaEntityChainFactory
{
    /// <summary>
    /// Ensures a Hub → Work → Edition chain exists for the given metadata
    /// and returns the <c>EditionId</c> to assign to the new MediaAsset.
    /// </summary>
    /// <param name="mediaType">Detected file type (Epub, Movie, etc.).</param>
    /// <param name="metadata">
    /// Scored metadata dictionary (keys: "title", "author", "year", etc.).
    /// The "title" key is used for Hub display name; may be absent.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The <c>editions.id</c> GUID to set on the MediaAsset.</returns>
    Task<Guid> EnsureEntityChainAsync(
        MediaType mediaType,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken ct = default);
}
