using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Models;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Providers.Services;

/// <summary>
/// Debounced orchestrator for writing <c>universe.xml</c> sidecar files.
///
/// <para>
/// When <see cref="NotifyEntityEnrichedAsync"/> is called, the service starts
/// (or resets) a per-universe timer. After a configurable quiet period
/// (default 5 seconds) with no further notifications for that universe,
/// the service loads all entities and relationships from the database,
/// builds a <see cref="UniverseSnapshot"/>, and writes it to
/// <c>{LibraryRoot}/.universe/{Label} ({Qid})/universe.xml</c>.
/// </para>
/// </summary>
public sealed class UniverseGraphWriterService : IUniverseGraphWriterService, IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _debounceTimers = new();
    private readonly TimeSpan _debounceInterval;

    private readonly INarrativeRootRepository _rootRepo;
    private readonly IFictionalEntityRepository _entityRepo;
    private readonly IEntityRelationshipRepository _relRepo;
    private readonly IUniverseSidecarWriter _sidecarWriter;
    private readonly IConfigurationLoader _configLoader;
    private readonly ISystemActivityRepository _activityRepo;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<UniverseGraphWriterService> _logger;

    public UniverseGraphWriterService(
        INarrativeRootRepository rootRepo,
        IFictionalEntityRepository entityRepo,
        IEntityRelationshipRepository relRepo,
        IUniverseSidecarWriter sidecarWriter,
        IConfigurationLoader configLoader,
        ISystemActivityRepository activityRepo,
        IEventPublisher eventPublisher,
        ILogger<UniverseGraphWriterService> logger)
    {
        ArgumentNullException.ThrowIfNull(rootRepo);
        ArgumentNullException.ThrowIfNull(entityRepo);
        ArgumentNullException.ThrowIfNull(relRepo);
        ArgumentNullException.ThrowIfNull(sidecarWriter);
        ArgumentNullException.ThrowIfNull(configLoader);
        ArgumentNullException.ThrowIfNull(activityRepo);
        ArgumentNullException.ThrowIfNull(eventPublisher);
        ArgumentNullException.ThrowIfNull(logger);

        _rootRepo       = rootRepo;
        _entityRepo     = entityRepo;
        _relRepo        = relRepo;
        _sidecarWriter  = sidecarWriter;
        _configLoader   = configLoader;
        _activityRepo   = activityRepo;
        _eventPublisher = eventPublisher;
        _logger         = logger;

        // Read debounce interval from hydration config; default 5 seconds.
        try
        {
            var hydration = configLoader.LoadHydration();
            var seconds = hydration.UniverseXmlWriteDebounceSeconds > 0
                ? hydration.UniverseXmlWriteDebounceSeconds
                : 5;
            _debounceInterval = TimeSpan.FromSeconds(seconds);
        }
        catch
        {
            _debounceInterval = TimeSpan.FromSeconds(5);
        }
    }

    /// <inheritdoc/>
    public Task NotifyEntityEnrichedAsync(string universeQid, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(universeQid))
            return Task.CompletedTask;

        // Cancel any pending debounce timer for this universe.
        if (_debounceTimers.TryRemove(universeQid, out var oldCts))
        {
            oldCts.Cancel();
            oldCts.Dispose();
        }

        var newCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _debounceTimers[universeQid] = newCts;

        // Fire-and-forget: wait for the debounce interval, then write.
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_debounceInterval, newCts.Token).ConfigureAwait(false);

                // Timer expired — remove from dictionary and write.
                _debounceTimers.TryRemove(universeQid, out _);

                await WriteUniverseXmlAsync(universeQid, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Debounce was reset — another notification arrived. Expected.
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Debounced universe XML write failed for {UniverseQid}", universeQid);
            }
        }, CancellationToken.None);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Builds a <see cref="UniverseSnapshot"/> from the database and writes
    /// <c>universe.xml</c> to the filesystem.
    /// </summary>
    private async Task WriteUniverseXmlAsync(string universeQid, CancellationToken ct)
    {
        var root = await _rootRepo.FindByQidAsync(universeQid, ct).ConfigureAwait(false);
        if (root is null)
        {
            _logger.LogDebug("Universe {Qid} not found in narrative_roots; skipping XML write", universeQid);
            return;
        }

        // Resolve library root for the output path.
        string? libraryRoot;
        try
        {
            var core = _configLoader.LoadCore();
            libraryRoot = core.LibraryRoot;
        }
        catch
        {
            libraryRoot = null;
        }

        if (string.IsNullOrWhiteSpace(libraryRoot))
        {
            _logger.LogDebug("LibraryRoot not configured; skipping universe XML write for {Qid}", universeQid);
            return;
        }

        // Load all entities in this universe.
        var entities = await _entityRepo.GetByUniverseAsync(universeQid, ct).ConfigureAwait(false);
        var entityQids = entities.Select(e => e.WikidataQid).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Load all relationships between those entities.
        var relationships = await _relRepo.GetByUniverseAsync(entityQids, ct).ConfigureAwait(false);

        // Build entity snapshots with work links and properties.
        var entitySnapshots = new List<FictionalEntitySnapshot>(entities.Count);
        foreach (var entity in entities)
        {
            var workLinks = await _entityRepo.GetWorkLinksAsync(entity.Id, ct).ConfigureAwait(false);

            // TODO: load canonical values for entity properties and performer links
            // once the canonical value repository supports fictional entity IDs.
            entitySnapshots.Add(new FictionalEntitySnapshot
            {
                Entity = entity,
                WorkLinks = workLinks.Select(wl => new WorkLinkSnapshot(wl.WorkQid, wl.WorkLabel)).ToList(),
                Properties = new Dictionary<string, string>(),
                PerformerPersonQid = null,
                PerformerWorkQid = null,
            });
        }

        // Build narrative hierarchy from narrative_roots.
        var children = await _rootRepo.GetChildrenAsync(universeQid, ct).ConfigureAwait(false);
        var hierarchy = new List<NarrativeHierarchyNode>();
        foreach (var child in children)
        {
            var grandchildren = await _rootRepo.GetChildrenAsync(child.Qid, ct).ConfigureAwait(false);
            hierarchy.Add(new NarrativeHierarchyNode
            {
                Qid = child.Qid,
                Label = child.Label,
                Level = child.Level,
                Children = grandchildren.Select(gc => new NarrativeHierarchyNode
                {
                    Qid = gc.Qid,
                    Label = gc.Label,
                    Level = gc.Level,
                }).ToList(),
            });
        }

        var snapshot = new UniverseSnapshot
        {
            Root = root,
            Entities = entitySnapshots,
            Relationships = relationships,
            Hierarchy = hierarchy,
            LastUpdated = DateTimeOffset.UtcNow,
        };

        // Write to .universe/{Label} ({Qid})/
        var folderName = SanitizeForFilesystem($"{root.Label} ({root.Qid})");
        var universeFolderPath = Path.Combine(libraryRoot, ".universe", folderName);

        await _sidecarWriter.WriteUniverseXmlAsync(universeFolderPath, snapshot, ct)
            .ConfigureAwait(false);

        // Log activity.
        try
        {
            await _activityRepo.LogAsync(new Domain.Entities.SystemActivityEntry
            {
                ActionType = Domain.Enums.SystemActionType.UniverseXmlUpdated,
                EntityType = "Universe",
                Detail = $"Universe XML updated for \"{root.Label}\" ({entities.Count} entities, {relationships.Count} relationships)",
            }, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to log universe XML update activity");
        }

        // Publish event.
        try
        {
            await _eventPublisher.PublishAsync(
                "UniverseGraphUpdated",
                new UniverseGraphUpdatedEvent(root.Qid, root.Label, entities.Count, relationships.Count),
                ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish UniverseGraphUpdated event");
        }

        _logger.LogInformation(
            "Debounced universe XML write complete for '{Label}' ({Qid}): {EntityCount} entities, {EdgeCount} edges",
            root.Label, root.Qid, entities.Count, relationships.Count);
    }

    /// <summary>
    /// Sanitizes a string for use as a filesystem path segment.
    /// </summary>
    private static string SanitizeForFilesystem(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new char[name.Length];
        for (var i = 0; i < name.Length; i++)
            sanitized[i] = Array.IndexOf(invalid, name[i]) >= 0 ? '_' : name[i];

        return new string(sanitized).TrimEnd('.', ' ');
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        foreach (var (_, cts) in _debounceTimers)
        {
            cts.Cancel();
            cts.Dispose();
        }
        _debounceTimers.Clear();

        await Task.CompletedTask;
    }
}
