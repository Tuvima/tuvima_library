using MediaEngine.Domain.Enums;

namespace MediaEngine.Domain.Entities;

/// <summary>
/// Tracks the execution state of a library synchronisation operation.
///
/// Spec: "A SyncJob that fails MUST be stored with a LastKnownState to allow
/// for resumption rather than a full restart."
///
/// <see cref="LastKnownState"/> is an opaque JSON payload whose schema is
/// defined by the synchronisation subsystem, not the domain model.
/// </summary>
public sealed class SyncJob
{
    /// <summary>Stable identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Current execution status.</summary>
    public SyncJobStatus Status { get; set; } = SyncJobStatus.Pending;

    /// <summary>
    /// Opaque JSON blob written on failure.
    /// Contains enough information for the sync subsystem to resume
    /// without re-processing already-completed work.
    /// Null when the job has never failed.
    /// </summary>
    public string? LastKnownState { get; set; }

    /// <summary>When this job was first created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the status was last updated.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
