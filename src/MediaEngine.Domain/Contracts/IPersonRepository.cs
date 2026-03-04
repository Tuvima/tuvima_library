using MediaEngine.Domain.Entities;

namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Persistence contract for <see cref="Person"/> records and their links
/// to media assets.
///
/// Implementations live in <c>MediaEngine.Storage</c>.
/// </summary>
public interface IPersonRepository
{
    /// <summary>
    /// Finds a person by name and role.
    /// Returns <c>null</c> if no matching person exists.
    /// Comparison is case-insensitive.
    /// </summary>
    /// <param name="name">The person's display name.</param>
    /// <param name="role">The role to match (e.g. <c>"Author"</c>).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<Person?> FindByNameAsync(
        string name,
        string role,
        CancellationToken ct = default);

    /// <summary>
    /// Inserts a new person record and returns it with any DB-generated fields populated.
    /// </summary>
    /// <param name="person">The person to create.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<Person> CreateAsync(
        Person person,
        CancellationToken ct = default);

    /// <summary>
    /// Updates the Wikidata enrichment fields for an existing person and sets
    /// <see cref="Person.EnrichedAt"/> to <see cref="DateTimeOffset.UtcNow"/>.
    /// </summary>
    /// <param name="personId">The person to update.</param>
    /// <param name="wikidataQid">The Wikidata Q-identifier (e.g. <c>"Q42"</c>), or <c>null</c> to clear.</param>
    /// <param name="headshotUrl">The Wikimedia Commons image URL, or <c>null</c> to clear.</param>
    /// <param name="biography">The Wikidata entity description, or <c>null</c> to clear.</param>
    /// <param name="ct">Cancellation token.</param>
    Task UpdateEnrichmentAsync(
        Guid personId,
        string? wikidataQid,
        string? headshotUrl,
        string? biography,
        CancellationToken ct = default);

    /// <summary>
    /// Creates a link between a media asset and a person in the
    /// <c>person_media_links</c> table.
    /// If the link already exists the call is a no-op (INSERT OR IGNORE).
    /// </summary>
    /// <param name="mediaAssetId">The media asset.</param>
    /// <param name="personId">The person.</param>
    /// <param name="role">The role the person plays in this asset.</param>
    /// <param name="ct">Cancellation token.</param>
    Task LinkToMediaAssetAsync(
        Guid mediaAssetId,
        Guid personId,
        string role,
        CancellationToken ct = default);

    /// <summary>
    /// Returns all persons linked to a given media asset.
    /// </summary>
    /// <param name="mediaAssetId">The media asset whose persons to retrieve.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<Person>> GetByMediaAssetAsync(
        Guid mediaAssetId,
        CancellationToken ct = default);
}
