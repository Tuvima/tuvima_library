namespace MediaEngine.Domain.Aggregates;

/// <summary>
/// The aggregate root for a group of related <see cref="Work"/> instances.
///
/// A Hub is the top-level organisational unit within the domain.
/// It may represent a series, a standalone title, a collection, or any
/// other logical grouping that makes sense to the user.
///
/// Hubs may optionally belong to a <see cref="Entities.Universe"/> via
/// <see cref="UniverseId"/>; a Hub MUST belong to at most one Universe.
///
/// Spec invariants:
/// • "A Work MUST NOT exist without a parent Hub." — Works reference HubId.
/// • "A deletion of a Hub MUST trigger … re-assignment of all associated
///   Works to an 'Unassigned' state." — implemented in the storage layer via
///   ON DELETE SET NULL and re-assignment to the System-Default Hub.
/// • "A Hub MUST belong to a maximum of one Universe." — UniverseId is nullable.
///
/// Maps to <c>hubs</c> in the Phase 4 schema.
/// </summary>
public sealed class Hub
{
    /// <summary>Stable identifier. PK in <c>hubs</c>.</summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Optional membership in a <see cref="Entities.Universe"/>.
    /// Null when this Hub does not belong to any Universe.
    /// Spec: "a Hub MUST belong to a maximum of one Universe."
    /// </summary>
    public Guid? UniverseId { get; set; }

    /// <summary>
    /// Human-readable name for display in the Dashboard and folder structure.
    /// Set from the title canonical value at organization time, or from the
    /// library.xml sidecar during Great Inhale.
    /// Null on hubs created before Phase 7.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>When this Hub was first registered in the system.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Wikidata coverage level for this Hub.
    /// Rich = QID found + 5+ properties filled.
    /// Limited = QID found but fewer than 5 properties.
    /// None = no QID found in Wikidata.
    /// Unknown = not yet checked (default for new/pre-existing hubs).
    /// Tracked in DB and sidecar XML for filtering and scheduled refresh.
    /// </summary>
    public string UniverseStatus { get; set; } = "Unknown";

    // -------------------------------------------------------------------------
    // Children
    // -------------------------------------------------------------------------

    /// <summary>
    /// All Works that belong to this Hub.
    /// This is the aggregate boundary: changes to a Hub and its Works
    /// MUST occur within a single transaction.
    /// Spec: Phase 2 – Scalability § Hub Atomic Zone.
    /// </summary>
    public List<Work> Works { get; set; } = [];
}
