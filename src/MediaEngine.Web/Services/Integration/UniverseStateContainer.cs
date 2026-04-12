using MediaEngine.Web.Models.ViewDTOs;

namespace MediaEngine.Web.Services.Integration;

// Navigation lanes defined in ContentLanes.cs — fixed content-type lanes replace Virtual Libraries.

/// <summary>
/// Scoped state container (one per Blazor Server circuit) that caches the
/// current Library view and surfaces real-time event data received from
/// the Engine API Intercom SignalR collection.
///
/// <para>
/// <b>Thread safety:</b> SignalR event handlers run on a background thread.
/// Components that subscribe to <see cref="OnStateChanged"/> MUST dispatch
/// re-renders via <c>InvokeAsync(StateHasChanged)</c> — never bare
/// <c>StateHasChanged()</c>.
/// </para>
/// </summary>
public sealed class UniverseStateContainer
{
    private List<CollectionViewModel>         _collections                       = [];
    private CollectionViewModel?              _selected;
    private UniverseViewModel?         _universe;
    private bool                       _loaded;
    private IngestionProgressEvent?    _ingestionProgress;
    private BatchProgressEvent?        _batchProgress;
    private WatchFolderActiveEvent?    _latestWatchFolderActivation;
    private string[]?                  _activeLaneMediaTypes;
    private readonly List<PersonEnrichedEvent> _personUpdates = [];
    private readonly List<ActivityEntry>       _activityLog   = [];
    private const int MaxActivityEntries = 100;

    // ── Read-only surface ─────────────────────────────────────────────────────

    public IReadOnlyList<CollectionViewModel> Collections              => _collections;
    public CollectionViewModel?               Selected          => _selected;

    /// <summary>
    /// Language code configured in the Engine (e.g. "en", "fr", "de").
    /// Used to build localised Wikipedia links. Defaults to "en" until the
    /// first successful status fetch.
    /// </summary>
    public string Language { get; set; } = "en";

    /// <summary>
    /// Flattened cross-media-type view built by <see cref="UniverseMapper"/>.
    /// Null until the first successful collection load; components should guard with
    /// <c>@if (State.Universe is { } u)</c>.
    /// </summary>
    public UniverseViewModel?          Universe          => _universe;

    public bool                        IsLoaded          => _loaded;

    /// <summary>
    /// Latest ingestion progress snapshot pushed via SignalR.
    /// Null when no ingestion is in progress or the circuit is freshly created.
    /// </summary>
    public IngestionProgressEvent?          IngestionProgress           => _ingestionProgress;

    /// <summary>
    /// Latest batch progress snapshot pushed via SignalR.
    /// Contains running counters, progress percentage, and estimated time remaining.
    /// Null when no batch is active.
    /// </summary>
    public BatchProgressEvent?              BatchProgress               => _batchProgress;

    public IReadOnlyList<PersonEnrichedEvent> RecentPersonUpdates        => _personUpdates;

    /// <summary>
    /// The most recent <c>"WatchFolderActive"</c> event received via SignalR.
    /// Null until the watch folder has been configured or changed in the current circuit.
    /// </summary>
    public WatchFolderActiveEvent?          LatestWatchFolderActivation => _latestWatchFolderActivation;

    /// <summary>
    /// Rolling log of plain-English activity entries from SignalR events.
    /// Most recent entries first. Capped at <see cref="MaxActivityEntries"/>.
    /// </summary>
    public IReadOnlyList<ActivityEntry>    ActivityLog                 => _activityLog;

    // ── Lane filter ──────────────────────────────────────────────────────────

    /// <summary>
    /// Collections filtered by the currently active content lane's media types.
    /// Returns all collections when no lane filter is active (Home page).
    /// </summary>
    public IReadOnlyList<CollectionViewModel> FilteredCollections =>
        _activeLaneMediaTypes is null or { Length: 0 }
            ? Collections
            : _collections.Where(h => SplitMediaTypes(h.MediaTypes).Any(mt =>
                _activeLaneMediaTypes.Any(f => string.Equals(f, mt, StringComparison.OrdinalIgnoreCase))))
              .ToList();

    /// <summary>
    /// Distinct set of media type strings across all collections in the library.
    /// Used by lane pages for content-aware display.
    /// </summary>
    public HashSet<string> AvailableMediaTypes =>
        new HashSet<string>(
            _collections.SelectMany(h => SplitMediaTypes(h.MediaTypes)),
            StringComparer.OrdinalIgnoreCase);

    /// <summary>Splits a comma-separated MediaTypes string into individual trimmed values.</summary>
    private static string[] SplitMediaTypes(string mediaTypes) =>
        string.IsNullOrWhiteSpace(mediaTypes)
            ? []
            : mediaTypes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// Sets the active lane media-type filter. Pass null or empty to show all (Home).
    /// Called by lane pages when they mount/navigate.
    /// </summary>
    public void SetLaneFilter(string[]? mediaTypes)
    {
        _activeLaneMediaTypes = mediaTypes;
        OnStateChanged?.Invoke();
    }

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fires whenever the collection list, selected collection, universe view, or
    /// ingestion progress changes.  May fire from a SignalR background thread —
    /// use <c>InvokeAsync(StateHasChanged)</c> in component handlers.
    /// </summary>
    public event Action? OnStateChanged;

    // ── Collection-list mutations ────────────────────────────────────────────────────

    /// <summary>
    /// Replaces the cached collection list and rebuilds the flattened
    /// <see cref="UniverseViewModel"/> via <see cref="UniverseMapper"/>.
    /// </summary>
    public void SetCollections(IEnumerable<CollectionViewModel> collections)
    {
        _collections     = collections.ToList();
        _universe = UniverseMapper.MapFromCollections(_collections);
        _loaded   = true;
        OnStateChanged?.Invoke();
    }

    public void SelectCollection(CollectionViewModel? collection)
    {
        _selected = collection;
        OnStateChanged?.Invoke();
    }

    /// <summary>
    /// Clears all cached data.  The next call to
    /// <c>UIOrchestratorService.GetCollectionsAsync()</c> will trigger a fresh API fetch.
    /// </summary>
    public void Invalidate()
    {
        _collections                = [];
        _selected            = null;
        _universe            = null;
        _loaded              = false;
        _ingestionProgress   = null;
        // Note: _activeLaneMediaTypes is NOT cleared on invalidate
        // so the user's lane selection persists across data refreshes.
        OnStateChanged?.Invoke();
    }

    // ── Real-time event sinks (called by UIOrchestratorService) ───────────────

    /// <summary>
    /// Called when an <c>"IngestionProgress"</c> event arrives on the Intercom collection.
    /// Updates the progress indicator and notifies subscribed components.
    /// </summary>
    public void PushIngestionProgress(IngestionProgressEvent ev)
    {
        _ingestionProgress = ev;

        // Only log stage transitions (Scanning/Complete), not every tick.
        if (ev.Stage is "Scanning" or "Complete")
        {
            var summary = ev.Stage == "Complete"
                ? $"Ingestion complete — {ev.ProcessedCount} files processed."
                : $"Scanning files… ({ev.ProcessedCount} found so far)";
            PushActivity(new ActivityEntry(
                DateTimeOffset.UtcNow, ActivityKind.IngestionProgress,
                "sync", summary));
        }

        OnStateChanged?.Invoke();
    }

    /// <summary>
    /// Called when a <c>"BatchProgress"</c> event arrives on the Intercom collection.
    /// Updates batch progress state for live progress bars and time-remaining display.
    /// </summary>
    public void PushBatchProgress(BatchProgressEvent ev)
    {
        _batchProgress = ev.IsComplete ? null : ev;
        OnStateChanged?.Invoke();
    }

    /// <summary>
    /// Called when a <c>"MediaAdded"</c> event arrives on the Intercom collection.
    /// Logs the event and invalidates the collection cache so the next navigation
    /// triggers a fresh load with the new Work included.
    /// </summary>
    public void PushMediaAdded(MediaAddedEvent ev)
    {
        PushActivity(new ActivityEntry(
            DateTimeOffset.UtcNow, ActivityKind.MediaAdded,
            "library_add",
            $"New {ev.MediaType.ToLowerInvariant()} added: \"{ev.Title}\"",
            ev.CollectionId is { } collectionId ? $"Assigned to Collection {collectionId:N}" : "Standalone (no Collection)"));
        Invalidate();
    }

    /// <summary>
    /// Called when an <c>"IngestionCompleted"</c> event arrives on the Intercom collection.
    /// Logs the event and invalidates the collection cache so the next navigation
    /// triggers a fresh load with the newly ingested file included.
    /// </summary>
    public void PushIngestionCompleted(IngestionCompletedClientEvent ev)
    {
        var fileName = Path.GetFileName(ev.FilePath);
        PushActivity(new ActivityEntry(
            DateTimeOffset.UtcNow, ActivityKind.MediaAdded,
            "library_add",
            $"Ingested {ev.MediaType.ToLowerInvariant()}: \"{fileName}\""));
        Invalidate();
    }

    /// <summary>
    /// Called when a <c>"PersonEnriched"</c> event arrives on the Intercom collection.
    /// Keeps a rolling buffer of the 50 most recent person updates.
    /// </summary>
    public void PushPersonEnriched(PersonEnrichedEvent ev)
    {
        _personUpdates.Add(ev);
        if (_personUpdates.Count > 50)
            _personUpdates.RemoveAt(0);

        PushActivity(new ActivityEntry(
            DateTimeOffset.UtcNow, ActivityKind.PersonEnriched,
            "person",
            $"Enriched person: {ev.Name}",
            ev.HeadshotUrl is not null ? "Portrait found on Wikimedia Commons" : null));

        OnStateChanged?.Invoke();
    }

    /// <summary>
    /// Called when a <c>"MetadataHarvested"</c> event arrives on the Intercom collection.
    /// Logs the event and invalidates the cache.
    /// </summary>
    public void PushMetadataHarvested(MetadataHarvestedEvent ev)
    {
        var fields = string.Join(", ", ev.UpdatedFields);
        PushActivity(new ActivityEntry(
            DateTimeOffset.UtcNow, ActivityKind.MetadataUpdated,
            "auto_awesome",
            $"Metadata updated by {ev.ProviderName}",
            $"Fields enriched: {fields}"));
        Invalidate();
    }

    /// <summary>
    /// Called when a <c>"WatchFolderActive"</c> event arrives on the Intercom collection.
    /// Updates <see cref="LatestWatchFolderActivation"/> so components can react
    /// to a watch folder change without a page reload.
    /// </summary>
    public void PushWatchFolderActive(WatchFolderActiveEvent ev)
    {
        _latestWatchFolderActivation = ev;

        PushActivity(new ActivityEntry(
            DateTimeOffset.UtcNow, ActivityKind.WatchFolderChanged,
            "folder_open",
            $"Watch folder updated: {ev.WatchDirectory}"));

        OnStateChanged?.Invoke();
    }

    // ── Activity log helpers ────────────────────────────────────────────────

    /// <summary>Inserts an entry at position 0 (most recent first) and trims overflow.</summary>
    private void PushActivity(ActivityEntry entry)
    {
        _activityLog.Insert(0, entry);
        if (_activityLog.Count > MaxActivityEntries)
            _activityLog.RemoveAt(_activityLog.Count - 1);
    }

    /// <summary>Adds a startup entry — called once by the orchestrator on connection.</summary>
    public void PushServerStarted()
    {
        PushActivity(new ActivityEntry(
            DateTimeOffset.UtcNow, ActivityKind.ServerStatus,
            "power_settings_new",
            "Dashboard connected to the Engine."));
        OnStateChanged?.Invoke();
    }
}
