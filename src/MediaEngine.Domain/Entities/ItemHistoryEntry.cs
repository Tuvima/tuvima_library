namespace MediaEngine.Domain.Entities;

/// <summary>
/// A single event in a media item's lifecycle history.
/// Append-only — entries are never modified or deleted.
/// </summary>
public sealed class ItemHistoryEntry
{
    public required string Id { get; init; }
    public required Guid EntityId { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
    public required string EventType { get; init; }
    public required string Label { get; init; }
    public string? Detail { get; init; }
}
