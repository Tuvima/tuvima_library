using MediaEngine.Domain.Entities;

namespace MediaEngine.Domain.Contracts;

/// <summary>
/// CRUD operations for <see cref="CharacterPortrait"/> records.
/// </summary>
public interface ICharacterPortraitRepository
{
    /// <summary>Get all portraits for a fictional character.</summary>
    Task<IReadOnlyList<CharacterPortrait>> GetByCharacterAsync(
        Guid fictionalEntityId, CancellationToken ct = default);

    /// <summary>Get all portraits featuring a specific person (actor).</summary>
    Task<IReadOnlyList<CharacterPortrait>> GetByPersonAsync(
        Guid personId, CancellationToken ct = default);

    /// <summary>Get the default portrait for a character, if any.</summary>
    Task<CharacterPortrait?> GetDefaultAsync(
        Guid fictionalEntityId, CancellationToken ct = default);

    /// <summary>
    /// Insert or update a portrait. Upsert keyed on (person_id, fictional_entity_id).
    /// </summary>
    Task UpsertAsync(CharacterPortrait portrait, CancellationToken ct = default);

    /// <summary>
    /// Set a specific portrait as the default for its character.
    /// Clears the default flag on all other portraits for the same character.
    /// </summary>
    Task SetDefaultAsync(Guid portraitId, CancellationToken ct = default);

    /// <summary>
    /// Batch-fetch portraits for multiple characters (for Universe Explorer views).
    /// </summary>
    Task<IReadOnlyList<CharacterPortrait>> GetByCharacterBatchAsync(
        IEnumerable<Guid> fictionalEntityIds, CancellationToken ct = default);
}
