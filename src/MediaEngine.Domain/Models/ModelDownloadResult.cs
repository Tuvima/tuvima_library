using MediaEngine.Domain.Enums;

namespace MediaEngine.Domain.Models;

public enum ModelDownloadOutcome
{
    Succeeded,
    AlreadyAvailable,
    Cancelled,
    Failed,
    NotStarted,
}

/// <summary>Terminal result for an owned model-artifact download.</summary>
public sealed record ModelDownloadResult(
    AiModelRole Role,
    ModelDownloadOutcome Outcome,
    string? ErrorMessage = null)
{
    public bool IsSuccess => Outcome is ModelDownloadOutcome.Succeeded
        or ModelDownloadOutcome.AlreadyAvailable;
}
