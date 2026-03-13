namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Persistence contract for multi-valued canonical fields.
///
/// Fields like genre, characters, and cast members can have multiple values
/// (e.g. "Science Fiction" + "Space Opera"). Each value is stored as a
/// separate row with an ordinal for display ordering and an optional QID
/// for entity-valued items.
///
/// This replaces the old <c>|||</c>-separated string storage in
/// <c>canonical_values</c> for multi-valued fields.
///
/// Implementations live in <c>MediaEngine.Storage</c>.
/// </summary>
public interface ICanonicalValueArrayRepository
{
    /// <summary>
    /// Replaces all values for a given (entityId, key) pair with the provided list.
    /// Existing rows are deleted first (full replacement, not merge).
    /// </summary>
    Task SetValuesAsync(
        Guid entityId,
        string key,
        IReadOnlyList<CanonicalArrayEntry> entries,
        CancellationToken ct = default);

    /// <summary>
    /// Returns all values for a given (entityId, key) pair, ordered by ordinal.
    /// </summary>
    Task<IReadOnlyList<CanonicalArrayEntry>> GetValuesAsync(
        Guid entityId,
        string key,
        CancellationToken ct = default);

    /// <summary>
    /// Returns all multi-valued entries for a given entity, grouped by key.
    /// </summary>
    Task<IReadOnlyDictionary<string, IReadOnlyList<CanonicalArrayEntry>>> GetAllByEntityAsync(
        Guid entityId,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes all multi-valued entries for a given entity.
    /// Used during orphan cleanup.
    /// </summary>
    Task DeleteByEntityAsync(Guid entityId, CancellationToken ct = default);
}

/// <summary>
/// A single entry in a multi-valued canonical field.
/// </summary>
public sealed class CanonicalArrayEntry
{
    /// <summary>Display order within the field (0-based).</summary>
    public int Ordinal { get; init; }

    /// <summary>The display value (e.g. "Science Fiction").</summary>
    public required string Value { get; init; }

    /// <summary>Optional Wikidata QID for entity-valued items (e.g. "Q24925").</summary>
    public string? ValueQid { get; init; }
}
