namespace MediaEngine.Providers;

/// <summary>
/// Broadcast when an external metadata provider successfully updates at least
/// one canonical field for an entity.
///
/// SignalR method name: <c>"MetadataHarvested"</c>
///
/// The Blazor Dashboard handles this event to invalidate its cached universe
/// state and trigger a card re-render (cover-art pop-in effect).
/// </summary>
/// <param name="EntityId">The entity whose metadata was updated.</param>
/// <param name="ProviderName">The adapter that produced the new claims (e.g. <c>"apple_books"</c>).</param>
/// <param name="UpdatedFields">The claim keys that were written (e.g. <c>["cover", "description"]</c>).</param>
public sealed record MetadataHarvestedEvent(
    Guid EntityId,
    string ProviderName,
    IReadOnlyList<string> UpdatedFields);

/// <summary>
/// Broadcast when the Wikidata adapter successfully enriches a person entity
/// with a headshot URL and/or biography.
///
/// SignalR method name: <c>"PersonEnriched"</c>
///
/// The Blazor Dashboard handles this event to update author/narrator cards
/// with the newly acquired headshot and Wikidata identifier.
/// </summary>
/// <param name="PersonId">The person entity that was enriched.</param>
/// <param name="Name">The person's display name.</param>
/// <param name="HeadshotUrl">Wikimedia Commons image URL, or <c>null</c> if not found.</param>
/// <param name="WikidataQid">The Wikidata Q-identifier (e.g. <c>"Q42"</c>), or <c>null</c>.</param>
public sealed record PersonEnrichedEvent(
    Guid PersonId,
    string Name,
    string? HeadshotUrl,
    string? WikidataQid);

// ── Hydration Pipeline Events ────────────────────────────────────────────

/// <summary>
/// Broadcast when a review queue item is created because the hydration pipeline
/// could not proceed automatically (disambiguation, low confidence, etc.).
///
/// SignalR method name: <c>"ReviewItemCreated"</c>
/// </summary>
/// <param name="ReviewItemId">The ID of the new review queue entry.</param>
/// <param name="EntityId">The entity that triggered the review.</param>
/// <param name="Trigger">The review trigger reason (e.g. "MultipleQidMatches").</param>
/// <param name="EntityTitle">Human-readable entity title (from canonical values), or <c>null</c>.</param>
public sealed record ReviewItemCreatedEvent(
    Guid ReviewItemId,
    Guid EntityId,
    string Trigger,
    string? EntityTitle);

/// <summary>
/// Broadcast when a review queue item is resolved or dismissed by a user.
///
/// SignalR method name: <c>"ReviewItemResolved"</c>
/// </summary>
/// <param name="ReviewItemId">The ID of the resolved/dismissed review queue entry.</param>
/// <param name="EntityId">The entity associated with the review item.</param>
/// <param name="Status">The new status ("Resolved" or "Dismissed").</param>
public sealed record ReviewItemResolvedEvent(
    Guid ReviewItemId,
    Guid EntityId,
    string Status);

/// <summary>
/// Broadcast when a hydration pipeline stage completes for an entity.
///
/// SignalR method name: <c>"HydrationStageCompleted"</c>
/// </summary>
/// <param name="EntityId">The entity being hydrated.</param>
/// <param name="Stage">The stage number (1, 2, or 3).</param>
/// <param name="ClaimsAdded">Number of claims added during this stage.</param>
/// <param name="ProviderName">The primary provider that contributed (for logging/display).</param>
public sealed record HydrationStageCompletedEvent(
    Guid EntityId,
    int Stage,
    int ClaimsAdded,
    string ProviderName);

// ── Universe Graph Events ────────────────────────────────────────────────

/// <summary>
/// Broadcast when a fictional entity (character, location, or organization) is
/// enriched from Wikidata SPARQL with its properties and relationships.
///
/// SignalR method name: <c>"FictionalEntityEnriched"</c>
/// </summary>
/// <param name="EntityId">The fictional entity's database ID.</param>
/// <param name="Label">Human-readable entity label (e.g. "Paul Atreides").</param>
/// <param name="EntitySubType">The entity sub-type: Character, Location, or Organization.</param>
/// <param name="UniverseQid">The narrative root QID this entity belongs to.</param>
public sealed record FictionalEntityEnrichedEvent(
    Guid EntityId,
    string Label,
    string EntitySubType,
    string? UniverseQid);

/// <summary>
/// Broadcast when a relationship edge is discovered between two fictional entities.
///
/// SignalR method name: <c>"RelationshipDiscovered"</c>
/// </summary>
/// <param name="SubjectQid">The subject entity's Wikidata QID.</param>
/// <param name="ObjectQid">The object entity's Wikidata QID.</param>
/// <param name="RelationshipType">The relationship type (e.g. "father", "member_of").</param>
/// <param name="UniverseQid">The narrative root QID for context.</param>
public sealed record RelationshipDiscoveredEvent(
    string SubjectQid,
    string ObjectQid,
    string RelationshipType,
    string? UniverseQid);

/// <summary>
/// Broadcast when a universe.xml sidecar file is written or updated after
/// a debounced enrichment cycle completes.
///
/// SignalR method name: <c>"UniverseGraphUpdated"</c>
/// </summary>
/// <param name="UniverseQid">The narrative root QID of the universe.</param>
/// <param name="Label">Human-readable universe label (e.g. "Dune universe").</param>
/// <param name="EntityCount">Total number of entities in the universe snapshot.</param>
/// <param name="EdgeCount">Total number of relationship edges in the snapshot.</param>
public sealed record UniverseGraphUpdatedEvent(
    string UniverseQid,
    string Label,
    int EntityCount,
    int EdgeCount);
