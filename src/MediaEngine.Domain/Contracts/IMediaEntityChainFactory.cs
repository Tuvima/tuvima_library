using MediaEngine.Domain.Enums;

namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Creates the Work → Edition chain required before a
/// <see cref="Aggregates.MediaAsset"/> can be inserted into the database.
///
/// The <c>media_assets</c> table has a NOT NULL FK to <c>editions(id)</c>,
/// which in turn references <c>works(id)</c>.  Works are created standalone
/// (hub_id = NULL); Hub assignment happens later during Stage 2 of the
/// hydration pipeline via Wikidata relationship intelligence.
/// </summary>
public interface IMediaEntityChainFactory
{
    /// <summary>
    /// Ensures a Work → Edition chain exists for the given metadata
    /// and returns the <c>EditionId</c> to assign to the new MediaAsset.
    /// </summary>
    /// <param name="mediaType">Detected file type (Epub, Movie, etc.).</param>
    /// <param name="metadata">
    /// Scored metadata dictionary (keys: "title", "author", "year", etc.).
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The <c>editions.id</c> GUID to set on the MediaAsset.</returns>
    Task<Guid> EnsureEntityChainAsync(
        MediaType mediaType,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken ct = default);
}
