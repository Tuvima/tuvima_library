using MediaEngine.Domain.Entities;

namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Persistence contract for <see cref="CanonicalValue"/> records.
///
/// Canonical values are the scoring engine's current best answer for each
/// metadata field of an entity. They are upserted on every re-score and
/// keyed by (EntityId, Key) — there is exactly one canonical value per
/// field per entity at any given time.
///
/// Implementations live in <c>MediaEngine.Storage</c>.
/// </summary>
public interface ICanonicalValueRepository
{
    /// <summary>
    /// Upserts a batch of canonical values.
    /// For each value, if a row with the same (EntityId, Key) already exists
    /// it is replaced; otherwise a new row is inserted.
    /// </summary>
    /// <param name="values">The canonical values to upsert. May be empty; no-op if so.</param>
    /// <param name="ct">Cancellation token.</param>
    Task UpsertBatchAsync(
        IReadOnlyList<CanonicalValue> values,
        CancellationToken ct = default);

    /// <summary>
    /// Returns all canonical values for a given entity, ordered by key ascending.
    /// </summary>
    /// <param name="entityId">The entity whose canonical values to retrieve.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<CanonicalValue>> GetByEntityAsync(
        Guid entityId,
        CancellationToken ct = default);

    /// <summary>
    /// Returns all canonical values for a batch of entities in a single query.
    /// The result is grouped by entity ID for O(1) per-job lookup.
    /// </summary>
    /// <param name="entityIds">Entity IDs to fetch canonical values for.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<CanonicalValue>>> GetByEntitiesAsync(
        IReadOnlyList<Guid> entityIds,
        CancellationToken ct = default);

    /// <summary>
    /// Returns all canonical values that have unresolved conflicts, ordered by
    /// most recently scored first.
    /// Spec: Phase B – Conflict Surfacing (B-05).
    /// </summary>
    Task<IReadOnlyList<CanonicalValue>> GetConflictedAsync(
        CancellationToken ct = default);

    /// <summary>
    /// Deletes all canonical values for the given <paramref name="entityId"/>.
    /// Used during orphan cleanup when the asset record is being removed.
    /// </summary>
    Task DeleteByEntityAsync(Guid entityId, CancellationToken ct = default);

    /// <summary>
    /// Deletes a single canonical value by entity ID and key.
    /// No-op if the key does not exist. Used to retract a value when the
    /// underlying resource is no longer available (e.g. cover download failed).
    /// </summary>
    Task DeleteByKeyAsync(Guid entityId, string key, CancellationToken ct = default);

    /// <summary>
    /// Returns entity IDs where the canonical value for the given key matches
    /// the given value (case-insensitive). Used by the Great Inhale people scanner
    /// to rebuild person-media links by matching author/narrator names.
    /// </summary>
    /// <param name="key">The canonical value key (e.g. "author", "narrator").</param>
    /// <param name="value">The value to match (case-insensitive).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<Guid>> FindByValueAsync(
        string key,
        string value,
        CancellationToken ct = default);

    /// <summary>
    /// Returns all canonical values where the key matches and the value
    /// starts with the given prefix. Empty prefix returns every value for the
    /// key. Used to find NF placeholder QIDs for incrementing the counter and
    /// to scan all values for aggregate profile signals.
    /// </summary>
    /// <param name="key">The canonical value key (e.g. "wikidata_qid").</param>
    /// <param name="prefix">The value prefix to match (e.g. "NF"), or empty for all values.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<CanonicalValue>> FindByKeyAndPrefixAsync(
        string key,
        string prefix,
        CancellationToken ct = default);

    /// <summary>
    /// Returns entity IDs that have at least one canonical value matching
    /// <paramref name="hasField"/> but do NOT yet have a canonical value
    /// matching <paramref name="missingField"/>.
    /// Used by background enrichment services to find un-processed entities.
    /// </summary>
    /// <param name="hasField">A field key the entity must already have (e.g. "description").</param>
    /// <param name="missingField">A field key the entity must NOT have yet (e.g. "themes").</param>
    /// <param name="limit">Maximum number of entity IDs to return.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<Guid>> GetEntitiesNeedingEnrichmentAsync(
        string hasField,
        string missingField,
        int limit,
        CancellationToken ct = default);
}
