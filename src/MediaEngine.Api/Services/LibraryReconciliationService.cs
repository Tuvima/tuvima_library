using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using MediaEngine.Domain;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Services;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Services;

/// <summary>
/// Background service that periodically scans all Normal-status assets and
/// verifies their files still exist on disk. Missing files are logged and
/// their directory artifacts (cover.jpg, empty folders) are
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
    private readonly ICollectionRepository              _collectionRepo;
    private readonly IEventPublisher              _publisher;
    private readonly IConfigurationLoader         _configLoader;
    private readonly AssetPathService             _assetPaths;
    private readonly ImagePathService             _imagePaths;
    private readonly IDatabaseConnection          _db;
    private readonly ILogger<LibraryReconciliationService> _logger;

    /// <summary>GUID folder pattern: 8-4-4-4-12 hex chars.</summary>
    [GeneratedRegex(@"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-", RegexOptions.Compiled)]
    private static partial Regex GuidFolderPattern();

    /// <summary>Category root folder names that should never be deleted.</summary>
    private static readonly HashSet<string> ProtectedFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        // .data/ contains staging, images, and database — never touch it.
        ".data",
        // Legacy hidden directories.
        ".people",
        // Library category folders.
        "Books", "Comics", "Videos", "Audio", "Other",
    };

    public LibraryReconciliationService(
        IMediaAssetRepository       assetRepo,
        IMetadataClaimRepository    claimRepo,
        ICanonicalValueRepository   canonicalRepo,
        ISystemActivityRepository   activityRepo,
        IPersonRepository           personRepo,
        IReviewQueueRepository      reviewRepo,
        ICollectionRepository              collectionRepo,
        IEventPublisher             publisher,
        IConfigurationLoader        configLoader,
        AssetPathService            assetPaths,
        ImagePathService            imagePaths,
        IDatabaseConnection         db,
        ILogger<LibraryReconciliationService> logger)
    {
        _assetRepo     = assetRepo;
        _claimRepo     = claimRepo;
        _canonicalRepo = canonicalRepo;
        _activityRepo  = activityRepo;
        _personRepo    = personRepo;
        _reviewRepo    = reviewRepo;
        _collectionRepo       = collectionRepo;
        _publisher     = publisher;
        _configLoader  = configLoader;
        _assetPaths    = assetPaths;
        _imagePaths    = imagePaths;
        _db            = db;
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

    /// <summary>Cron expression for the reconciliation schedule. Default: 5 AM daily.</summary>
    private const string DefaultSchedule = "0 5 * * *";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("LibraryReconciliationService started — schedule: {Schedule}", DefaultSchedule);

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
                    var schedule = maintenance.Schedules.GetValueOrDefault("library_reconciliation", DefaultSchedule);
                    if (string.IsNullOrWhiteSpace(schedule)) schedule = DefaultSchedule;
                    var delay = CronScheduler.UntilNext(schedule, TimeSpan.FromHours(24));
                    await Task.Delay(delay, stoppingToken);
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

            // Capture the parent Work's QID before deleting DB records (needed for .images/ cleanup).
            string? wikidataQid = null;
            try
            {
                using var conn = _db.CreateConnection();
                using var qidCmd = conn.CreateCommand();
                qidCmd.CommandText = """
                    SELECT w.wikidata_qid
                    FROM works w
                    INNER JOIN editions e ON e.work_id = w.id
                    WHERE e.id = @editionId
                    LIMIT 1
                    """;
                qidCmd.Parameters.AddWithValue("@editionId", asset.EditionId.ToString());
                wikidataQid = qidCmd.ExecuteScalar() as string;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Reconciliation: could not resolve QID for asset {Id}", asset.Id);
            }

            // Clean filesystem artifacts (legacy: cover/hero alongside media file).
            var editionFolder = Path.GetDirectoryName(asset.FilePathRoot);
            if (!string.IsNullOrEmpty(editionFolder) && Directory.Exists(editionFolder))
            {
                SafeDeleteFile(Path.Combine(editionFolder, "cover.jpg"));
                SafeDeleteFile(Path.Combine(editionFolder, "hero.jpg"));
                SafeDeleteFile(Path.Combine(editionFolder, "library.xml"));
                TryDeleteEmptyDirectory(editionFolder);

                var collectionFolder = Path.GetDirectoryName(editionFolder);
                if (!string.IsNullOrEmpty(collectionFolder) && Directory.Exists(collectionFolder))
                {
                    SafeDeleteFile(Path.Combine(collectionFolder, "library.xml"));
                    TryDeleteEmptyDirectory(collectionFolder);
                }
            }

            // Delete DB records.
            await _claimRepo.DeleteByEntityAsync(asset.Id, ct);
            await _canonicalRepo.DeleteByEntityAsync(asset.Id, ct);
            await _assetRepo.DeleteAsync(asset.Id, ct);

            // Clean .images/ directory for this work — after DB delete so sibling check is valid.
            CleanOrphanWorkImages(wikidataQid, asset.Id);

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
        // After deleting MediaAssets, remove any Editions / Works / Collections that
        // are now empty so they stop appearing on the home page.
        int hierarchyPruned = 0;
        if (missingCount > 0)
        {
            hierarchyPruned = await _collectionRepo.PruneOrphanedHierarchyAsync(ct);
            if (hierarchyPruned > 0)
            {
                _logger.LogInformation(
                    "Reconciliation: pruned {Count} orphaned hierarchy records (editions/works/collections)",
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
            // shallow organization template caused collectionFolder to resolve to the category root.
            staleSidecarsCount = CleanStaleRootSidecars(core.LibraryRoot);

            // Re-run empty folder pruning after people cleanup may have emptied folders.
            if (orphanPeopleCount > 0 || staleGuidFoldersCount > 0)
                foldersCleanedCount += await PruneEmptyFoldersAsync(core.LibraryRoot, ct);
        }

        sw.Stop();

        // Log a single consolidated FolderCleaned entry when any folder maintenance pass
        // cleaned up at least one item. Sub-items in ChangesJson let the Dashboard
        // show detail without flooding the timeline with separate entries per pass.
        int totalFolderOps = foldersCleanedCount + orphanPeopleCount + staleGuidFoldersCount + staleSidecarsCount;
        if (totalFolderOps > 0)
        {
            var subItems = new List<object>();
            if (foldersCleanedCount > 0)
                subItems.Add(new { label = "Empty folders pruned", count = foldersCleanedCount });
            if (orphanPeopleCount > 0)
                subItems.Add(new { label = "Orphaned people folders removed", count = orphanPeopleCount });
            if (staleGuidFoldersCount > 0)
                subItems.Add(new { label = "Stale GUID folders removed", count = staleGuidFoldersCount });
            if (staleSidecarsCount > 0)
                subItems.Add(new { label = "Stale root sidecars cleaned", count = staleSidecarsCount });

            await _activityRepo.LogAsync(new SystemActivityEntry
            {
                ActionType  = SystemActionType.FolderCleaned,
                EntityType  = "System",
                Detail      = $"Cleaned {totalFolderOps} folder(s)",
                ChangesJson = JsonSerializer.Serialize(new { items = subItems }),
            }, ct);
        }

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
        // open invalidate their collection cache and refresh the home page.
        if (missingCount > 0)
        {
            try
            {
                await _publisher.PublishAsync(SignalREvents.MediaRemoved, new
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

        // Individual pass logging removed — the orchestrator logs a single
        // consolidated FolderCleaned entry after all passes complete.

        return cleaned;
    }

    // ── Pass 2: Orphaned People Cleanup ─────────────────────────────────────

    /// <summary>
    /// Scans .people/ subfolders. Identifies each folder by parsing the
    /// "Name (QID)" naming convention and looking up the QID in the database.
    /// If no matching person exists in the DB, or the person has zero media
    /// asset links, the folder is deleted.
    ///
    /// Note: person.xml sidecars are no longer written by the engine (the
    /// database is the authoritative store). Validation uses the QID from
    /// the folder name instead.
    /// </summary>
    private async Task<int> CleanOrphanedPeopleAsync(string libraryRoot, CancellationToken ct)
    {
        int cleaned = 0;

        // Scan the canonical .data/assets/people/ path and the legacy directories.
        // Canonical path: folder name = person id.
        // Legacy centralized path: folder name = QID.
        // Legacy path: folder name = "Name (QID)" format.
        var peopleDirsToScan = new List<(string root, string format)>();

        if (Directory.Exists(_assetPaths.PeopleRoot))
            peopleDirsToScan.Add((_assetPaths.PeopleRoot, "canonical"));

        var legacyCentralRoot = Path.Combine(_imagePaths.ImagesRoot, "people");
        if (Directory.Exists(legacyCentralRoot))
            peopleDirsToScan.Add((legacyCentralRoot, "legacy-central"));

        var legacyPeopleRoot = Path.Combine(libraryRoot, ".people");
        if (Directory.Exists(legacyPeopleRoot))
            peopleDirsToScan.Add((legacyPeopleRoot, "legacy-folder"));

        foreach (var (peopleRoot, format) in peopleDirsToScan)
        {
            foreach (var subDir in Directory.GetDirectories(peopleRoot))
            {
                ct.ThrowIfCancellationRequested();

                var folderName = Path.GetFileName(subDir);
                Person? person = null;

                if (string.Equals(format, "canonical", StringComparison.OrdinalIgnoreCase))
                {
                    if (Guid.TryParse(folderName, out var personId))
                        person = await _personRepo.FindByIdAsync(personId, ct);
                }
                else if (string.Equals(format, "legacy-central", StringComparison.OrdinalIgnoreCase))
                {
                    if (folderName.Length >= 2
                        && (folderName[0] == 'Q' || folderName[0] == 'q')
                        && folderName[1..].All(char.IsDigit))
                    {
                        person = await _personRepo.FindByQidAsync(folderName.ToUpperInvariant(), ct);
                    }
                }
                else
                {
                    // Legacy format: parse QID from "Name (Qxxxxx)" folder naming convention.
                    var qid = ExtractQidFromFolderName(folderName);
                    if (!string.IsNullOrEmpty(qid))
                        person = await _personRepo.FindByQidAsync(qid, ct);

                    // Fallback: try legacy person.xml if it exists (migration path).
                    if (person is null)
                    {
                        var personXml = Path.Combine(subDir, "person.xml");
                        if (File.Exists(personXml))
                        {
                            var personId = ReadPersonIdFromXml(personXml);
                            if (personId is not null)
                                person = await _personRepo.FindByIdAsync(personId.Value, ct);

                            if (person is null)
                            {
                                var xmlName = ReadPersonNameFromXml(personXml);
                                if (!string.IsNullOrWhiteSpace(xmlName))
                                    person = await _personRepo.FindByNameAsync(xmlName, ct);
                            }
                        }
                    }

                    // Fallback: temporary folders use "tmp-{guid}" pattern.
                    if (person is null && folderName.StartsWith("tmp-", StringComparison.OrdinalIgnoreCase))
                    {
                        var guidPart = folderName[4..];
                        if (Guid.TryParse(guidPart, out var tmpId))
                            person = await _personRepo.FindByIdAsync(tmpId, ct);
                    }
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
        }

        // Individual pass logging removed — the orchestrator logs a single
        // consolidated FolderCleaned entry after all passes complete.

        return cleaned;
    }

    /// <summary>
    /// Extracts a Wikidata QID from a "Name (Qxxxxx)" folder name.
    /// Returns <c>null</c> if no QID pattern is found.
    /// </summary>
    private static string? ExtractQidFromFolderName(string folderName)
    {
        // Match "(Qnnn)" at the end of the folder name.
        var openParen = folderName.LastIndexOf('(');
        var closeParen = folderName.LastIndexOf(')');
        if (openParen < 0 || closeParen <= openParen + 1)
            return null;

        var candidate = folderName[(openParen + 1)..closeParen].Trim();
        if (candidate.Length >= 2
            && (candidate[0] == 'Q' || candidate[0] == 'q')
            && candidate[1..].All(char.IsDigit))
        {
            return candidate.ToUpperInvariant();
        }

        return null;
    }

    // ── Pass 3: Stale GUID Folder Cleanup ───────────────────────────────────

    /// <summary>
    /// Scans the legacy .people/ directory for subfolders that look like GUIDs (old-format naming).
    /// If the person exists in the DB with a name-based folder elsewhere,
    /// the GUID folder is deleted. Only applies to the legacy .people/ path — the new
    /// .data/images/people/{QID}/ format does not use GUID-named folders.
    /// </summary>
    private async Task<int> CleanStaleGuidFoldersAsync(string libraryRoot, CancellationToken ct)
    {
        // Only applies to the legacy .people/ directory.
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

        // Individual pass logging removed — the orchestrator logs a single
        // consolidated FolderCleaned entry after all passes complete.

        return cleaned;
    }

    // ── Pass 4: Stale Category-Root Sidecar Cleanup ─────────────────────────

    /// <summary>
    /// Deletes <c>library.xml</c> files found directly inside category root folders
    /// (e.g., <c>{LibraryRoot}/Books/library.xml</c>).
    ///
    /// These stray collection sidecars are created by <c>AutoOrganizeService</c> when a
    /// shallow organization template (such as <c>{Category}/{Author}/{Title}{Ext}</c>)
    /// causes <c>GetDirectoryName(editionFolder)</c> to resolve to the category root
    /// rather than a dedicated collection subfolder.  The guard added to AutoOrganizeService
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

    /// <summary>
    /// Removes the .images/ directory for an orphaned work asset.
    /// For QID-keyed works, only deletes the directory if no other work in the DB shares the QID.
    /// For provisional works, always deletes the provisional slot.
    /// Best-effort — never throws.
    /// </summary>
    private void CleanOrphanWorkImages(string? wikidataQid, Guid assetId)
    {
        try
        {
            if (!string.IsNullOrEmpty(wikidataQid) &&
                !wikidataQid.StartsWith("NF", StringComparison.OrdinalIgnoreCase))
            {
                bool qidStillReferenced;
                try
                {
                    using var conn = _db.CreateConnection();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT COUNT(1) FROM works WHERE wikidata_qid = @qid";
                    cmd.Parameters.AddWithValue("@qid", wikidataQid);
                    qidStillReferenced = Convert.ToInt64(cmd.ExecuteScalar()) > 0;
                }
                catch
                {
                    // If we can't check, err on the side of keeping the directory
                    return;
                }

                // Always delete this asset's image subdirectory.
                var assetDir = _imagePaths.GetWorkImageDir(wikidataQid, assetId);
                if (Directory.Exists(assetDir))
                    Directory.Delete(assetDir, recursive: true);

                // If no other works reference this QID, clean up the empty QID parent.
                if (!qidStillReferenced)
                {
                    var qidParent = Path.GetDirectoryName(assetDir);
                    if (qidParent is not null && Directory.Exists(qidParent)
                        && !Directory.EnumerateFileSystemEntries(qidParent).Any())
                    {
                        try { Directory.Delete(qidParent); } catch { /* best-effort */ }
                    }
                }
            }
            else
            {
                var provDir = _imagePaths.GetWorkImageDir(null, assetId);
                if (Directory.Exists(provDir))
                    Directory.Delete(provDir, recursive: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to clean .images/ for orphaned asset {AssetId}", assetId);
        }
    }
}

/// <summary>
/// Result of a library reconciliation scan.
/// </summary>
public sealed record ReconciliationResult(int TotalScanned, int MissingCount, long ElapsedMs);
