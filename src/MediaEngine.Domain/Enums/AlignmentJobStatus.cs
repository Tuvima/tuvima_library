namespace MediaEngine.Domain.Enums;

/// <summary>
/// Lifecycle status of a WhisperSync alignment job.
/// </summary>
public enum AlignmentJobStatus
{
    /// <summary>Job queued but not yet started.</summary>
    Pending,

    /// <summary>Whisper inference is currently running.</summary>
    Processing,

    /// <summary>Alignment completed successfully.</summary>
    Completed,

    /// <summary>Job failed (error message stored on the entity).</summary>
    Failed
}
