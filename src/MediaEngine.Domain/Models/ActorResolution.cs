namespace MediaEngine.Domain.Models;

/// <summary>Resolved actor for a character at a specific point in the timeline.</summary>
public sealed record ActorResolution(
    string? ActorPersonQid,
    string? ActorLabel,
    string? HeadshotUrl,
    string? StartTime,
    string? EndTime,
    string? ContextWorkQid);
