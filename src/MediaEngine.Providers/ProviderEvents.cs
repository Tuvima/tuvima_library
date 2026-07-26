namespace MediaEngine.Providers;

// Universe graph payloads remain frozen in their existing owner until the
// dedicated Universe/Chronicle contract packet is authorized.

public sealed record FictionalEntityEnrichedEvent(
    Guid EntityId,
    string Label,
    string EntitySubType,
    string? UniverseQid);

public sealed record RelationshipDiscoveredEvent(
    string SubjectQid,
    string ObjectQid,
    string RelationshipType,
    string? UniverseQid);

public sealed record UniverseGraphUpdatedEvent(
    string UniverseQid,
    string Label,
    int EntityCount,
    int EdgeCount);
