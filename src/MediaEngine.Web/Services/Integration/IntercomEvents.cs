namespace MediaEngine.Web.Services.Integration;

/// <summary>
/// Payload broadcast by the Engine via SignalR when a new Work is
/// committed to the library.
///
/// SignalR method name: <c>"MediaAdded"</c>
///
/// Example handler:
/// <code>
/// hubConnection.On&lt;MediaAddedEvent&gt;("MediaAdded", ev => { ... });
/// </code>
/// </summary>
/// <param name="WorkId">The newly ingested Work's unique identifier.</param>
/// <param name="HubId">The Hub this Work was assigned to, or <c>null</c> if standalone.</param>
/// <param name="MediaType">Domain media-type string (e.g. "Epub", "Video", "Cbz").</param>
/// <param name="Title">Best-available title for immediate display before a full hub refresh.</param>
public sealed record MediaAddedEvent(
    Guid   WorkId,
    Guid?  HubId,
    string MediaType,
    string Title);

/// <summary>
/// Payload broadcast during an active ingestion run to report incremental progress.
///
/// SignalR method name: <c>"IngestionProgress"</c>
///
/// Example handler:
/// <code>
/// hubConnection.On&lt;IngestionProgressEvent&gt;("IngestionProgress", ev => { ... });
/// </code>
/// </summary>
/// <param name="CurrentFile">Short display name of the file currently being processed.</param>
/// <param name="ProcessedCount">Number of files processed so far in this run.</param>
/// <param name="TotalCount">Total files discovered for this run (0 if still scanning).</param>
/// <param name="Stage">
/// Human-readable stage label.  One of:
/// <c>"Scanning"</c> | <c>"Hashing"</c> | <c>"Processing"</c> | <c>"Complete"</c>
/// </param>
public sealed record IngestionProgressEvent(
    string CurrentFile,
    int    ProcessedCount,
    int    TotalCount,
    string Stage);

/// <summary>
/// Payload broadcast when an external provider successfully updates metadata
/// for a library entity (cover art, narrator, series, etc.).
///
/// SignalR method name: <c>MetadataHarvested</c>
///
/// The Dashboard invalidates its cached universe state on receipt, triggering
/// a re-render so cover art and other fields pop in as they arrive.
/// </summary>
/// <param name=EntityId>The entity whose metadata was updated.</param>
/// <param name=ProviderName>The adapter that produced the claims (e.g. <c>apple_books</c>).</param>
/// <param name=UpdatedFields>Claim keys that changed (e.g. <c>[cover,description]</c>).</param>
public sealed record MetadataHarvestedEvent(
    Guid   EntityId,
    string ProviderName,
    IReadOnlyList<string> UpdatedFields);

/// <summary>
/// Payload broadcast when the Wikidata adapter enriches a person entity
/// with a headshot URL, biography, and/or Q-identifier.
///
/// SignalR method name: <c>PersonEnriched</c>
///
/// The Dashboard uses this event to update author/narrator cards with the
/// newly acquired portrait image.
/// </summary>
/// <param name=PersonId>The person entity that was enriched.</param>
/// <param name=Name>The person's display name.</param>
/// <param name=HeadshotUrl>Wikimedia Commons image URL, or <c>null</c> if not found.</param>
/// <param name=WikidataQid>Wikidata Q-identifier (e.g. <c>Q42</c>), or <c>null</c>.</param>
public sealed record PersonEnrichedEvent(
    Guid    PersonId,
    string  Name,
    string? HeadshotUrl,
    string? WikidataQid);

/// <summary>
/// Payload broadcast when the Watch Folder is updated at runtime — either on first
/// configuration or after the user changes the path in Settings.
///
/// SignalR method name: <c>"WatchFolderActive"</c>
/// </summary>
/// <param name="WatchDirectory">The absolute path now being monitored.</param>
/// <param name="ActivatedAt">UTC timestamp of when the new path became active.</param>
public sealed record WatchFolderActiveEvent(
    string         WatchDirectory,
    DateTimeOffset ActivatedAt);

/// <summary>
/// Payload broadcast by the Engine via SignalR when a file has been
/// successfully ingested (hashed, scored, and — if AutoOrganize is on — moved
/// into the library).
///
/// SignalR method name: <c>"IngestionCompleted"</c>
///
/// The Dashboard invalidates its cached universe state on receipt so the
/// hub grid refreshes with the newly ingested file.
/// </summary>
/// <param name="FilePath">The path of the ingested file (may be the organised destination).</param>
/// <param name="MediaType">Domain media-type string (e.g. "Epub", "Movie").</param>
/// <param name="CompletedAt">UTC timestamp of when ingestion completed.</param>
public sealed record IngestionCompletedClientEvent(
    string         FilePath,
    string         MediaType,
    DateTimeOffset CompletedAt);

/// <summary>
/// Payload broadcast periodically by the Engine's <c>FolderHealthService</c> when
/// the accessibility of the Watch Folder or Library Root changes.
///
/// SignalR method name: <c>"FolderHealthChanged"</c>
///
/// The Dashboard <c>LibrariesTab</c> subscribes to this event and updates the
/// green/red status dots next to each folder path in real-time.
/// </summary>
/// <param name="Path">Absolute path of the folder being checked.</param>
/// <param name="IsAccessible">Whether the folder exists and is readable.</param>
/// <param name="HasRead">True if the process can read from the folder.</param>
/// <param name="HasWrite">True if the process can write to the folder.</param>
/// <param name="CheckedAt">UTC timestamp of the last health check.</param>
public sealed record FolderHealthChangedEvent(
    string         Path,
    bool           IsAccessible,
    bool           HasRead,
    bool           HasWrite,
    DateTimeOffset CheckedAt);

// ── Hydration Pipeline Events ────────────────────────────────────────────────

/// <summary>
/// Payload broadcast when a review queue item is created because the hydration
/// pipeline could not proceed automatically (disambiguation needed or low confidence).
///
/// SignalR method name: <c>"ReviewItemCreated"</c>
///
/// The Dashboard uses this event to update the review count badge on the profile
/// avatar and to refresh the Needs Review queue if the user is viewing it.
/// </summary>
/// <param name="ReviewItemId">The review queue entry ID.</param>
/// <param name="EntityId">The entity that triggered the review.</param>
/// <param name="Trigger">The trigger reason (e.g. "LowConfidence", "MultipleQidMatches").</param>
/// <param name="EntityTitle">Best-available title for display, or null.</param>
public sealed record ReviewItemCreatedEvent(
    Guid    ReviewItemId,
    Guid    EntityId,
    string  Trigger,
    string? EntityTitle);

/// <summary>
/// Payload broadcast when a review queue item is resolved or dismissed.
///
/// SignalR method name: <c>"ReviewItemResolved"</c>
///
/// The Dashboard decrements the badge count and removes the item from the queue.
/// </summary>
/// <param name="ReviewItemId">The review queue entry ID.</param>
/// <param name="EntityId">The entity whose review was resolved.</param>
/// <param name="Status">New status: "Resolved" or "Dismissed".</param>
public sealed record ReviewItemResolvedEvent(
    Guid   ReviewItemId,
    Guid   EntityId,
    string Status);

/// <summary>
/// Payload broadcast when an individual stage of the hydration pipeline completes
/// for an entity. Used by the Dashboard to show live stage-by-stage progress.
///
/// SignalR method name: <c>"HydrationStageCompleted"</c>
/// </summary>
/// <param name="EntityId">The entity being hydrated.</param>
/// <param name="Stage">The stage number (1, 2, or 3).</param>
/// <param name="ClaimsAdded">Number of claims added in this stage.</param>
/// <param name="ProviderName">The provider or stage label that completed.</param>
public sealed record HydrationStageCompletedEvent(
    Guid   EntityId,
    int    Stage,
    int    ClaimsAdded,
    string ProviderName);

// ── Chronicle Engine Events ────────────────────────────────────────────────

/// <summary>
/// Payload broadcast when the Lore Delta check discovers Wikidata revisions
/// that are newer than the library's cached versions.
///
/// SignalR method name: <c>"LoreDeltaDiscovered"</c>
///
/// The Dashboard shows an amber banner on the Chronicle Explorer page with
/// the count of changed entities.
/// </summary>
/// <param name="UniverseQid">The narrative root QID that was checked.</param>
/// <param name="ChangedCount">Number of entities with updated Wikidata revisions.</param>
public sealed record LoreDeltaDiscoveredEvent(
    string UniverseQid,
    int    ChangedCount);