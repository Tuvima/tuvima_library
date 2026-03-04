namespace MediaEngine.Domain.Enums;

/// <summary>
/// Execution state of a <see cref="Entities.SyncJob"/>.
/// Spec: Phase 2 – Failure Handling § Job Recovery.
/// </summary>
public enum SyncJobStatus
{
    /// <summary>Job has been created but not yet started.</summary>
    Pending,

    /// <summary>Job is actively executing.</summary>
    Running,

    /// <summary>
    /// Job encountered an unrecoverable error.
    /// A <c>LastKnownState</c> payload MUST be stored to allow resumption.
    /// </summary>
    Failed,

    /// <summary>Job finished successfully.</summary>
    Completed,
}
