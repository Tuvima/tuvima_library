namespace MediaEngine.Domain.Models;

/// <summary>
/// A pending person signal awaiting batch Wikidata verification.
/// Stored in the <c>pending_person_signals</c> table between inline
/// extraction and deferred batch verification.
/// </summary>
public sealed record PendingPersonSignal
{
    public required Guid Id { get; init; }
    public required Guid EntityId { get; init; }
    public required string Name { get; init; }
    public required string Role { get; init; }
    public required string Source { get; init; }
    public string? Pattern { get; init; }
    public required string MediaType { get; init; }
    public required string CreatedAt { get; init; }
}
