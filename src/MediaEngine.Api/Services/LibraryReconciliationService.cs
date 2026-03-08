using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Services;

/// <summary>
/// Background service that periodically scans all Normal-status assets and
/// verifies their files still exist on disk. Missing files are logged and
/// their directory artifacts (cover.jpg, library.xml, empty folders) are
/// cleaned up.
///
/// Also performs three folder-maintenance passes:
/// <list type="bullet">
///   <item><b>Empty folder pruning:</b> Walks the Library Root bottom-up and
///     deletes any empty directory (skips category roots and .people root).</item>
///   <item><b>Orphaned people cleanup:</b> Scans .people/ subfolders; if the
///     person has no matching DB record or zero media links, the folder is deleted.</item>
///   <item><b>Stale GUID folder cleanup:</b> Scans .people/ for GUID-named
///     subfolders left from the old naming scheme. If the person now has a
///     name-based folder, the GUID folder is deleted.</item>
/// </list>
///
/// Also exposes <see cref="ReconcileAsync"/> for manual trigger via API.
///
/// Follows the same loop pattern as <see cref="ActivityPruningService"/>.
/// </summary>
public sealed partial class LibraryReconciliationService : BackgroundService, IReconciliationService
{
    private readonly IMediaAssetRepository       _assetRepo;
    private readonly IMetadataClaimRepository     _claimRepo;
    private readonly ICanonicalValueRepository    _canonicalRepo;
    private readonly ISystemActivityRepository    _activityRepo;
    private readonly IPersonRepository            _personRepo;
    private readonly IReviewQueueRepository       _reviewRepo;
    private readonly IHubRepository              _hubRepo;
    private readonly IEventPublisher              _publisher;
    private readonly IConfigurationLoader         _configLoader;
    private readonly ILogger<LibraryReconciliationService> _logger;

    /// <summary>GUID folder pattern: 8-4-4-4-12 hex chars.</summary>
    [GeneratedRegex(@"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-", RegexOptions.Compiled)]
    private static partial Regex GuidFolderPattern();

    /// <summary>Category root folder names that should never be deleted.</summary>
    private static readonly HashSet<string> ProtectedFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        ".people", "Books", "Comics", "Videos", "Audio", "Other",
    };

    public LibraryReconciliationService(
        IMediaAssetRepository       assetRepo,
        IMetadataClaimRepository    claimRepo,
        ICanonicalValueRepository   canonicalRepo,
        ISystemActivityRepository   activityRepo,
        IPersonRepository           personRepo,
        IReviewQueueRepository      reviewRepo,
        IHubRepository              hubRepo,
        IEventPublisher             publisher,
        IConfigurationLoader        configLoader,
        ILogger<LibraryReconciliationService> logger)
    {
        _assetRepo     = assetRepo;
        _claimRepo     = claimRepo;
        _canonicalRepo = canonicalRepo;
        _activityRepo  = activityRepo;
        _personRepo    = personRepo;
        _reviewRepo    = reviewRepo;
        _hubRepo       = hubRepo;
        _publisher     = publisher;
        _configLoader  = configLoader;
        _logger        = logger;
    }

    // ── IReconciliationService ────────────────────────────────────────────

    /// <inheritdoc />
    async Task<ReconciliationSummary> IReconciliationService.ReconcileAsync(CancellationToken ct)
    {
        var result = await ReconcileAsync(ct).ConfigureAwait(false);
        return new ReconciliationSummary(result.TotalScanned, result.MissingCount, result.ElapsedMs);
    }

    // ── BackgroundService ─────────────────────────────────────────────────

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("LibraryReconciliationService started");

        // The IngestionEngine already runs reconciliation at startup (Step 2),
        // so this background loop waits the full interval before its first run
        // to avoid a duplicate reconciliation within seconds of boot.

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var maintenance = _configLoader.LoadMaintenance();
                var intervalHours = maintenance.ReconciliationIntervalHours;

                if (intervalHours > 0)
                {
                    await Task.Delay(TimeSpan.FromHours(intervalHours), stoppingToken);
                    await ReconcileAsync(stoppingToken);
                }
                else
                {
                    // Disabled — check again in 1 hour in case config changes.
                    await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Library reconciliation failed; will retry next cycle");
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }
    }

    /// <summary>
    /// Scans all Normal-status assets, verifies files exist on disk,
    /// and cleans up any orphans found.
    /// </summary>
    public async Task<ReconciliationResult> ReconcileAsync(CancellationToken ct = default)
    {
        var sw        = Stopwatch.StartNew();
        var startedAt = DateTimeOffset.UtcNow;
        var assets    = await _assetRepo.ListByStatusAsync(AssetStatus.Normal, ct);
        int missingCount = 0;

        // Collect missing-file details for the completion summary.
        var missingFiles = new List<object>();

        foreach (var asset in assets)
        {
            ct.ThrowIfCancellationRequested();

            if (File.Exists(asset.FilePathRoot))
                continue;

            _logger.LogInformation(
                "Reconciliation: file missing for asset {Id} at {Path}",
                asset.Id, asset.FilePathRoot);

            // Clean filesystem artifacts.
            var editionFolder = Path.GetDirectoryName(asset.FilePathRoot);
            if (!string.IsNullOrEmpty(editionFolder) && Directory.Exists(editionFolder))
            {
                SafeDeleteFile(Path.Combine(editionFolder, "cover.jpg"));
                SafeDeleteFile(Path.Combine(editionFolder, "library.xml"));
                TryDeleteEmptyDirectory(editionFolder);

                var hubFolder = Path.GetDirectoryName(editionFolder);
                if (!string.IsNullOrEmpty(hubFolder) && Directory.Exists(hubFolder))
                {
                    SafeDeleteFile(Path.Combine(hubFolder, "library.xml"));
                    TryDeleteEmptyDirectory(hubFolder);
                }
            }

            // Delete DB records.
            await _claimRepo.DeleteByEntityAsync(asset.Id, ct);
            await _canonicalRepo.DeleteByEntityAsync(asset.Id, ct);
            await _assetRepo.DeleteAsync(asset.Id, ct);

            // Dismiss any pending review queue items for this entity so they
            // no longer appear in the Needs Review tab after the file is gone.
            await _reviewRepo.DismissAllByEntityAsync(asset.Id, ct);

            // Collect details for the completion summary (no separate per-file entry needed).
            missingFiles.Add(new
            {
                entity_id = asset.Id.ToString(),
                filename  = Path.GetFileName(asset.FilePathRoot),
                action    = "Records and artifacts deleted",
            });

            // Still log a per-file entry so the entity ID is recorded in the activity log
            // (used by the Dashboard to hide the reprocess button for deleted assets).
            await _activityRepo.LogAsync(new SystemActivityEntry
            {
                ActionType = SystemActionType.ReconciliationMissing,
                EntityId   = asset.Id,
                EntityType = "MediaAsset",
                Detail     = $"Missing file detected: {Path.GetFileName(asset.FilePathRoot)}",
            }, ct);

            missingCount++;
        }

        // ── Database hierarchy pruning ────────────────────────────────────────
        // After deleting MediaAssets, remove any Editions / Works / Hubs that
        // are now empty so they stop appearing on the home page.
        int hierarchyPruned = 0;
        if (missingCount > 0)
        {
            hierarchyPruned = await _hubRepo.PruneOrphanedHierarchyAsync(ct);
            if (hierarchyPruned > 0)
            {
                _logger.LogInformation(
                    "Reconciliation: pruned {Count} orphaned hierarchy records (editions/works/hubs)",
                    hierarchyPruned);
            }
        }

        // ── Folder Maintenance Passes ───────────────────────────────────────

        var core = _configLoader.LoadCore();
        int foldersCleanedCount    = 0;
        int orphanPeopleCount      = 0;
        int staleGuidFoldersCount  = 0;
        int staleSidecarsCount     = 0;

        if (!string.IsNullOrWhiteSpace(core.LibraryRoot) && Directory.Exists(core.LibraryRoot))
        {
            // Pass 1: Empty folder pruning (bottom-up).
            foldersCleanedCount = await PruneEmptyFoldersAsync(core.LibraryRoot, ct);

            // Pass 2: Orphaned people cleanup.
            orphanPeopleCount = await CleanOrphanedPeopleAsync(core.LibraryRoot, ct);

            // Pass 3: Stale GUID folder cleanup.
            staleGuidFoldersCount = await CleanStaleGuidFoldersAsync(core.LibraryRoot, ct);

            // Pass 4: Stale category-root sidecar cleanup.
            // Removes library.xml files that were incorrectly written directly inside a
            // category folder (e.g., Books/library.xml) by AutoOrganizeService when a
            // shallow organization template caused hubFolder to resolve to the category root.
            staleSidecarsCount = CleanStaleRootSidecars(core.LibraryRoot);

            // Re-run empty folder pruning after people cleanup may have emptied folders.
            if (orphanPeopleCount > 0 || staleGuidFoldersCount > 0)
                foldersCleanedCount += await PruneEmptyFoldersAsync(core.LibraryRoot, ct);
        }

        sw.Stop();

        // Log completion with full details so the Dashboard can show a single grouped entry.
        await _activityRepo.LogAsync(new SystemActivityEntry
        {
            ActionType  = SystemActionType.ReconciliationCompleted,
            EntityType  = "System",
            Detail      = $"Reconciliation complete — scanned {assets.Count}, {missingCount} missing, {hierarchyPruned} hierarchy rows pruned, {foldersCleanedCount} empty folders cleaned, {orphanPeopleCount} orphan people removed, {staleGuidFoldersCount} stale GUID folders removed, {staleSidecarsCount} stale root sidecars cleaned",
            ChangesJson = JsonSerializer.Serialize(new
            {
                total_scanned        = assets.Count,
                missing_count        = missingCount,
                hierarchy_pruned     = hierarchyPruned,
                folders_cleaned      = foldersCleanedCount,
                orphan_people        = orphanPeopleCount,
                stale_guid_folders   = staleGuidFoldersCount,
                stale_root_sidecars  = staleSidecarsCount,
                elapsed_ms           = sw.ElapsedMilliseconds,
                started_at           = startedAt.ToString("O"),
                missing_files        = missingFiles,
            }),
        }, ct);

        // Broadcast a library-changed event so Dashboard circuits that are already
        // open invalidate their hub cache and refresh the home page.
        if (missingCount > 0)
        {
            try
            {
                await _publisher.PublishAsync("MediaRemoved", new
                {
                    source        = "reconciliation",
                    removed_count = missingCount,
                }, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to publish MediaRemoved SignalR event after reconciliation");
            }
        }

        _logger.LogInformation(
            "Reconciliation complete: {Total} scanned, {Missing} missing, {HierarchyPruned} hierarchy rows pruned, " +
            "{FoldersCleaned} empty folders, {OrphanPeople} orphan people, " +
            "{StaleGuid} stale GUID folders, {StaleSidecars} stale root sidecars, {Elapsed}ms",
            assets.Count, missingCount, hierarchyPruned, foldersCleanedCount,
            orphanPeopleCount, staleGuidFoldersCount, staleSidecarsCount, sw.ElapsedMilliseconds);

        return new ReconciliationResult(assets.Count, missingCount, sw.ElapsedMilliseconds);
    }

    // ── Pass 1: Empty Folder Pruning ────────────────────────────────────────

    /// <summary>
    /// Walks the Library Root tree bottom-up. Deletes any empty directory
    /// (no files, no subdirectories). Skips the library root itself,
    /// .people root, and category roots (Books, Videos, etc.).
    /// </summary>
    private async Task<int> PruneEmptyFoldersAsync(string libraryRoot, CancellationToken ct)
    {
        int cleaned = 0;

        // Bottom-up: process deepest directories first by sorting by depth descending.
        var allDirs = Directory.EnumerateDirectories(libraryRoot, "*", SearchOption.AllDirectories)
            .OrderByDescending(d => d.Count(c => c is '\\' or '/'))
            .ToList();

        foreach (var dir in allDirs)
        {
            ct.ThrowIfCancellationRequested();

            // Skip protected roots (one level below library root).
            var parent = Path.GetDirectoryName(dir);
            if (string.Equals(parent, libraryRoot, StringComparison.OrdinalIgnoreCase))
            {
                var folderName = Path.GetFileName(dir);
                if (ProtectedFolders.Contains(folderName))
                    continue;
            }

            try
            {
                if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                {
                    Directory.Delete(dir);
                    cleaned++;
                    _logger.LogDebug("Pruned empty folder: {Path}", dir);
                }
            }
            catch (IOException ex)
            {
                _logger.LogDebug(ex, "Could not prune folder: {Path}", dir);
            }
        }

        if (cleaned > 0)
        {
            await _activityRepo.LogAsync(new SystemActivityEntry
            {
                ActionType = SystemActionType.FolderCleaned,
                EntityType = "System",
                Detail     = $"Pruned {cleaned} empty folder(s)",
            }, ct);
        }

        return cleaned;
    }

    // ── Pass 2: Orphaned People Cleanup ─────────────────────────────────────

    /// <summary>
    /// Scans .people/ subfolders. For each folder, reads person.xml to extract
    /// the person ID. If no matching person exists in the DB, or the person has
    /// zero media asset links, the folder is deleted.
    /// </summary>
    private async Task<int> CleanOrphanedPeopleAsync(string libraryRoot, CancellationToken ct)
    {
        var peopleRoot = Path.Combine(libraryRoot, ".people");
        if (!Directory.Exists(peopleRoot))
            return 0;

        int cleaned = 0;

        foreach (var subDir in Directory.GetDirectories(peopleRoot))
        {
            ct.ThrowIfCancellationRequested();

            var personXml = Path.Combine(subDir, "person.xml");
            if (!File.Exists(personXml))
            {
                // No person.xml → orphaned folder, safe to remove.
                SafeDeleteDirectory(subDir);
                cleaned++;
                _logger.LogDebug("Removed orphan people folder (no person.xml): {Path}", subDir);
                continue;
            }

            // Read the person ID from person.xml (may be null for old-format XML).
            var personId = ReadPersonIdFromXml(personXml);

            // Try to find the person in the DB: by ID, or by name if no <id>.
            Person? person = null;
            if (personId is not null)
            {
                person = await _personRepo.FindByIdAsync(personId.Value, ct);
            }
            else
            {
                // Old-format person.xml without <id>. Try to match by name.
                var xmlName = ReadPersonNameFromXml(personXml);
                if (string.IsNullOrWhiteSpace(xmlName))
                {
                    _logger.LogDebug("Could not parse person.xml in: {Path}", subDir);
                    continue;
                }

                // Try common roles.
                person = await _personRepo.FindByNameAsync(xmlName, "Author", ct)
                      ?? await _personRepo.FindByNameAsync(xmlName, "Narrator", ct);

                if (person is not null)
                    personId = person.Id;
            }

            if (person is null)
            {
                SafeDeleteDirectory(subDir);
                cleaned++;
                _logger.LogDebug("Removed orphan people folder (no DB record): {Path}", subDir);
                continue;
            }

            // Check if person has any media asset links.
            var linkCount = await _personRepo.CountMediaLinksAsync(person.Id, ct);
            if (linkCount == 0)
            {
                // Person exists but has zero media links → orphan.
                await _personRepo.DeleteAsync(person.Id, ct);
                SafeDeleteDirectory(subDir);
                cleaned++;
                _logger.LogDebug("Removed orphan person (zero media links): {Name}", person.Name);
            }
        }

        if (cleaned > 0)
        {
            await _activityRepo.LogAsync(new SystemActivityEntry
            {
                ActionType = SystemActionType.OrphanCleaned,
                EntityType = "Person",
                Detail     = $"Cleaned {cleaned} orphaned people folder(s)",
            }, ct);
        }

        return cleaned;
    }

    // ── Pass 3: Stale GUID Folder Cleanup ───────────────────────────────────

    /// <summary>
    /// Scans .people/ for subfolders that look like GUIDs (old-format naming).
    /// If the person exists in the DB with a name-based folder elsewhere,
    /// the GUID folder is deleted.
    /// </summary>
    private async Task<int> CleanStaleGuidFoldersAsync(string libraryRoot, CancellationToken ct)
    {
        var peopleRoot = Path.Combine(libraryRoot, ".people");
        if (!Directory.Exists(peopleRoot))
            return 0;

        int cleaned = 0;

        foreach (var subDir in Directory.GetDirectories(peopleRoot))
        {
            ct.ThrowIfCancellationRequested();

            var folderName = Path.GetFileName(subDir);
            if (!GuidFolderPattern().IsMatch(folderName))
                continue; // Not a GUID folder — skip.

            // This folder is GUID-named. Check if the person has a name-based folder.
            var personXml = Path.Combine(subDir, "person.xml");
            var personId = ReadPersonIdFromXml(personXml);
            var personName = ReadPersonNameFromXml(personXml);

            // Try to resolve the person: by ID first, then by name from the XML.
            Person? person = null;
            if (personId is not null)
                person = await _personRepo.FindByIdAsync(personId.Value, ct);

            // If no <id> in old-format XML, try to find the person by name.
            if (person is null && !string.IsNullOrWhiteSpace(personName))
            {
                var sanitizedName = SanitizeForFilesystem(personName);
                var nameFolder = Path.Combine(peopleRoot, sanitizedName);

                // If a name-based folder already exists for this name, the GUID
                // folder is the stale leftover from old code.
                if (Directory.Exists(nameFolder)
                    && !string.Equals(nameFolder, subDir, StringComparison.OrdinalIgnoreCase))
                {
                    SafeDeleteDirectory(subDir);
                    cleaned++;
                    _logger.LogDebug(
                        "Removed stale GUID folder (no <id>, name match '{Name}'): {Path}",
                        personName, subDir);
                    continue;
                }
            }

            if (person is null)
            {
                // Person doesn't exist → orphan cleanup already handled or will handle it.
                continue;
            }

            // Check if a name-based folder exists for this person.
            if (!string.IsNullOrWhiteSpace(person.Name))
            {
                var sanitizedName = SanitizeForFilesystem(person.Name);
                var nameFolder = Path.Combine(peopleRoot, sanitizedName);

                if (Directory.Exists(nameFolder)
                    && !string.Equals(nameFolder, subDir, StringComparison.OrdinalIgnoreCase))
                {
                    // Name-based folder exists and is different from this GUID folder.
                    SafeDeleteDirectory(subDir);
                    cleaned++;
                    _logger.LogDebug(
                        "Removed stale GUID folder for person '{Name}': {Path}",
                        person.Name, subDir);
                }
            }
        }

        if (cleaned > 0)
        {
            await _activityRepo.LogAsync(new SystemActivityEntry
            {
                ActionType = SystemActionType.FolderCleaned,
                EntityType = "Person",
                Detail     = $"Cleaned {cleaned} stale GUID-named people folder(s)",
            }, ct);
        }

        return cleaned;
    }

    // ── Pass 4: Stale Category-Root Sidecar Cleanup ─────────────────────────

    /// <summary>
    /// Deletes <c>library.xml</c> files found directly inside category root folders
    /// (e.g., <c>{LibraryRoot}/Books/library.xml</c>).
    ///
    /// These stray hub sidecars are created by <c>AutoOrganizeService</c> when a
    /// shallow organization template (such as <c>{Category}/{Author}/{Title}{Ext}</c>)
    /// causes <c>GetDirectoryName(editionFolder)</c> to resolve to the category root
    /// rather than a dedicated hub subfolder.  The guard added to AutoOrganizeService
    /// prevents new strays from being created; this pass cleans up any that exist from
    /// prior runs.
    ///
    /// Only examines direct children of <see cref="LibraryRoot"/> that are non-hidden
    /// directories (i.e., actual category folders).  The <c>.people</c> directory is
    /// skipped because its <c>person.xml</c> files are intentional.
    /// </summary>
    private int CleanStaleRootSidecars(string libraryRoot)
    {
        int cleaned = 0;

        try
        {
            foreach (var categoryDir in Directory.EnumerateDirectories(libraryRoot))
            {
                var folderName = Path.GetFileName(categoryDir);

                // Skip hidden folders (e.g., .people — its XML files are intentional).
                if (string.IsNullOrEmpty(folderName) || folderName.StartsWith('.'))
                    continue;

                var staleXml = Path.Combine(categoryDir, "library.xml");
                if (!File.Exists(staleXml))
                    continue;

                SafeDeleteFile(staleXml);
                cleaned++;
                _logger.LogInformation(
                    "Removed stale category-root sidecar: {Path}", staleXml);
            }
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Error during stale root sidecar cleanup");
        }

        return cleaned;
    }

    // ── Shared Helpers ──────────────────────────────────────────────────────

    private static void SafeDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
    }

    private static void TryDeleteEmptyDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
                Directory.Delete(path);
        }
        catch (IOException) { }
    }

    private static void SafeDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (IOException) { }
    }

    /// <summary>
    /// Reads the person ID from a person.xml file's identity/id element.
    /// </summary>
    private static Guid? ReadPersonIdFromXml(string xmlPath)
    {
        try
        {
            if (!File.Exists(xmlPath)) return null;
            var doc = System.Xml.Linq.XDocument.Load(xmlPath);
            var idText = doc.Root?.Element("identity")?.Element("id")?.Value;
            return Guid.TryParse(idText, out var id) ? id : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Reads the person name from a person.xml file's identity/name element.
    /// Handles both old-format (no &lt;id&gt;) and new-format person.xml files.
    /// </summary>
    private static string? ReadPersonNameFromXml(string xmlPath)
    {
        try
        {
            if (!File.Exists(xmlPath)) return null;
            var doc = System.Xml.Linq.XDocument.Load(xmlPath);
            var name = doc.Root?.Element("identity")?.Element("name")?.Value
                    ?? doc.Root?.Element("name")?.Value; // old format fallback
            return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Sanitizes a string for use as a filesystem path segment.
    /// Mirrors the same logic in <c>MetadataHarvestingService</c>.
    /// </summary>
    private static string SanitizeForFilesystem(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new char[name.Length];
        for (var i = 0; i < name.Length; i++)
            sanitized[i] = Array.IndexOf(invalid, name[i]) >= 0 ? '_' : name[i];

        return new string(sanitized).TrimEnd('.', ' ');
    }
}

/// <summary>
/// Result of a library reconciliation scan.
/// </summary>
public sealed record ReconciliationResult(int TotalScanned, int MissingCount, long ElapsedMs);
