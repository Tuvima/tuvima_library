namespace MediaEngine.Domain.Contracts;

using MediaEngine.Domain.Entities;

/// <summary>
/// CRUD contract for the entity timeline tables (<c>entity_events</c> and
/// <c>entity_field_changes</c>). Tracks the full life history of every
/// entity — pipeline provenance, refreshes, user edits, and sync writebacks.
/// </summary>
public interface IEntityTimelineRepository
{
    // ── Events ─────────────────────────────────────────────────────────

    /// <summary>Inserts a new event.</summary>
    Task InsertEventAsync(EntityEvent evt, CancellationToken ct = default);

    /// <summary>Inserts multiple events in a single transaction.</summary>
    Task InsertEventsAsync(IReadOnlyList<EntityEvent> events, CancellationToken ct = default);

    /// <summary>Returns all events for an entity, newest first.</summary>
    Task<IReadOnlyList<EntityEvent>> GetEventsByEntityAsync(Guid entityId, CancellationToken ct = default);

    /// <summary>Returns the most recent event for an entity at a specific pipeline stage.</summary>
    Task<EntityEvent?> GetLatestEventAsync(Guid entityId, int stage, CancellationToken ct = default);

    /// <summary>Returns the most recent event per stage for an entity (up to 4 rows for pipeline stages 0-3).</summary>
    Task<IReadOnlyList<EntityEvent>> GetCurrentPipelineStateAsync(Guid entityId, CancellationToken ct = default);

    /// <summary>Returns a single event by ID.</summary>
    Task<EntityEvent?> GetEventByIdAsync(Guid eventId, CancellationToken ct = default);

    // ── Field Changes ──────────────────────────────────────────────────

    /// <summary>Inserts a batch of field changes for an event.</summary>
    Task InsertFieldChangesAsync(IReadOnlyList<EntityFieldChange> changes, CancellationToken ct = default);

    /// <summary>Returns all field changes for an event.</summary>
    Task<IReadOnlyList<EntityFieldChange>> GetFieldChangesByEventAsync(Guid eventId, CancellationToken ct = default);

    /// <summary>Returns the full field history for an entity (all changes across all events), newest first.</summary>
    Task<IReadOnlyList<EntityFieldChange>> GetFieldHistoryAsync(Guid entityId, CancellationToken ct = default);

    /// <summary>Returns field changes for a specific field on an entity, newest first.</summary>
    Task<IReadOnlyList<EntityFieldChange>> GetFieldHistoryAsync(Guid entityId, string field, CancellationToken ct = default);

    /// <summary>
    /// Returns field changes flagged as file originals for a specific sync writeback event.
    /// Used by the revert feature to restore original file metadata.
    /// </summary>
    Task<IReadOnlyList<EntityFieldChange>> GetFileOriginalsForEventAsync(Guid eventId, CancellationToken ct = default);

    // ── Queries for Vault List ─────────────────────────────────────────

    /// <summary>
    /// Returns the most recent Stage 2 (Wikidata) event for each entity in the given list.
    /// Used by the Vault list view to populate the "Resolution" column efficiently.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, EntityEvent>> GetLatestStage2EventsAsync(
        IReadOnlyList<Guid> entityIds, CancellationToken ct = default);

    // ── Maintenance ────────────────────────────────────────────────────

    /// <summary>
    /// Deletes events older than the retention threshold, preserving the most recent
    /// event per entity per stage. Field changes cascade via FK.
    /// </summary>
    Task<int> CullOldEventsAsync(TimeSpan retention, CancellationToken ct = default);

    /// <summary>Deletes all events and field changes for an entity.</summary>
    Task DeleteByEntityAsync(Guid entityId, CancellationToken ct = default);
}
