using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MediaEngine.Domain;
using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Events;
using MediaEngine.Domain.Models;
using MediaEngine.Domain.Services;
using MediaEngine.Ingestion.Contracts;
using MediaEngine.Ingestion.Models;
using MediaEngine.Ingestion.Services;
using MediaEngine.Intelligence.Contracts;
using MediaEngine.Intelligence.Models;
using MediaEngine.Processors.Contracts;

namespace MediaEngine.Ingestion;

/// <summary>
/// Headless <see cref="BackgroundService"/> that orchestrates the full file
/// ingestion pipeline.  Also implements <see cref="IIngestionEngine"/> so that
/// the host can call <see cref="DryRunAsync"/> from test / maintenance code
/// without starting the live watcher.
///
/// ──────────────────────────────────────────────────────────────────
/// Pipeline (per accepted file — spec: Phase 7 – Lifecycle Automation)
/// ──────────────────────────────────────────────────────────────────
///
///  1. Dequeue <see cref="IngestionCandidate"/> from <see cref="DebounceQueue"/>.
///  2. Skip failed candidates (log the failure).
///  3. Handle Deleted events: mark asset orphaned in the repository.
///  4. Hash the file via <see cref="IAssetHasher"/>.
///  5. Duplicate check via <see cref="IMediaAssetRepository.FindByHashAsync"/>.
///  6. Run <see cref="IProcessorRegistry.ProcessAsync"/> → <see cref="Processors.Models.ProcessorResult"/>.
///  7. Quarantine corrupt files (log; no further processing).
///  8. Convert <see cref="Processors.Models.ExtractedClaim"/>s → <see cref="MetadataClaim"/> rows.
///  9. Score via <see cref="IScoringEngine"/> → populate <c>candidate.Metadata</c>.
/// 10. Insert <see cref="MediaAsset"/> into repository (INSERT OR IGNORE).
/// 11. If <c>AutoOrganize</c>: calculate destination and execute move.
/// 12. If <c>WriteBack</c>: write resolved metadata (and cover art) back into the file.
///
/// Spec: Phase 7 – Interfaces § IIngestionEngine.
/// </summary>
public sealed class IngestionEngine : BackgroundService, IIngestionEngine
{
    // Stable GUID representing the local-file processor as a "provider".
    // Used as ProviderId in MetadataClaim rows so the scoring engine can weight it.
    private static readonly Guid LocalProcessorProviderId = WellKnownProviders.LocalProcessor;

    // Extensions that are never media files and must be skipped by the batch
    // scanner and polling sweep.  These can appear in watch folders as sidecar
    // data (e.g. MANIFEST.json written by the test generator) or alongside
    // media files (e.g. cover art, subtitle tracks).
    private static readonly HashSet<string> NonMediaExtensions =
        new(StringComparer.OrdinalIgnoreCase)
    {
        ".json", ".xml", ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp",
        ".txt", ".md", ".nfo", ".srt", ".vtt", ".ass", ".sub", ".idx",
        ".log", ".db", ".db-wal", ".db-shm", ".lnk", ".ini", ".cfg",
    };

    private readonly IFileWatcher          _watcher;
    private readonly DebounceQueue         _debounce;
    private readonly IAssetHasher          _hasher;
    private readonly IProcessorRegistry    _processors;
    private readonly IScoringEngine        _scorer;
    private readonly IFileOrganizer        _organizer;
    private readonly IEnumerable<IMetadataTagger> _taggers;
    private readonly IMediaAssetRepository _assetRepo;
    private readonly IBackgroundWorker     _worker;
    private readonly IEventPublisher       _publisher;
    private readonly IngestionOptions      _options;
    private readonly ILogger<IngestionEngine> _logger;

    // Phase 9: claim/canonical persistence + external metadata harvesting.
    private readonly IMetadataClaimRepository    _claimRepo;
    private readonly ICanonicalValueRepository   _canonicalRepo;
    private readonly IHydrationPipelineService   _pipeline;
    private readonly IRecursiveIdentityService   _identity;

    // Hub → Work → Edition scaffold creation.
    private readonly IMediaEntityChainFactory _chainFactory;

    // Review queue — created when confidence is too low or category is "Other".
    private readonly IReviewQueueRepository _reviewRepo;

    // Activity ledger — records every significant ingestion event.
    private readonly ISystemActivityRepository _activityRepo;

    // Reconciliation — cleans orphaned DB records before the initial scan.
    private readonly IReconciliationService _reconciliation;

    // Hero banner generation — creates cinematic hero.jpg from cover art.
    private readonly IHeroBannerGenerator _heroGenerator;

    // Centralized organization gate — single source of truth for promotion eligibility.
    private readonly IOrganizationGate _gate;

    // Centralized image path resolution — all artwork lives under {libraryRoot}/.images/.
    private readonly ImagePathService? _imagePathService;

    // Per-file ingestion lifecycle log — tracks each file from detection to completion.
    private readonly IIngestionLogRepository _ingestionLog;

    // AI-powered filename cleaning and media type classification (Sprint 2).
    private readonly ISmartLabeler _smartLabeler;
    private readonly IMediaTypeAdvisor _typeAdvisor;

    // Pipeline provenance — records lifecycle events for timeline.
    private readonly IEntityTimelineRepository _timelineRepo;

    // Config-driven thresholds — replaces hardcoded 0.85 literals.
    private readonly ScoringConfiguration _scoringConfig;

    // Ingestion batch tracking — creates batch records and emits BatchProgress events.
    private readonly IIngestionBatchRepository _batchRepo;

    // Centralized concurrency guard (Principle 5: formalized lock hierarchy).
    // Replaces inline ConcurrentDictionary<string, SemaphoreSlim> instances.
    // Lock order: folder → hash (see ConcurrencyGuard doc for full hierarchy).
    private readonly ConcurrencyGuard _concurrencyGuard = new();

    // Collect-then-process: accumulate FSW/poll events, create batch after quiet period.
    private readonly List<FileEvent> _fswBuffer = [];
    private readonly HashSet<string> _fswBufferedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _enqueuedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _fswBufferLock = new();
    private Timer? _fswFlushTimer;
    private static readonly TimeSpan FswQuietPeriod = TimeSpan.FromSeconds(30);

    public IngestionEngine(
        IFileWatcher              watcher,
        DebounceQueue             debounce,
        IAssetHasher              hasher,
        IProcessorRegistry        processors,
        IScoringEngine            scorer,
        IFileOrganizer            organizer,
        IEnumerable<IMetadataTagger> taggers,
        IMediaAssetRepository     assetRepo,
        IBackgroundWorker         worker,
        IEventPublisher           publisher,
        IOptions<IngestionOptions> options,
        ILogger<IngestionEngine>  logger,
        IMetadataClaimRepository   claimRepo,
        ICanonicalValueRepository  canonicalRepo,
        IHydrationPipelineService  pipeline,
        IRecursiveIdentityService  identity,
        IMediaEntityChainFactory   chainFactory,
        IReviewQueueRepository     reviewRepo,
        ISystemActivityRepository  activityRepo,
        IReconciliationService     reconciliation,
        IHeroBannerGenerator       heroGenerator,
        IOrganizationGate          gate,
        IIngestionLogRepository    ingestionLog,
        ISmartLabeler             smartLabeler,
        IMediaTypeAdvisor         typeAdvisor,
        IEntityTimelineRepository  timelineRepo,
        ScoringConfiguration       scoringConfig,
        IIngestionBatchRepository  batchRepo,
        ImagePathService?          imagePathService = null)
    {
        _watcher          = watcher;
        _debounce         = debounce;
        _hasher           = hasher;
        _processors       = processors;
        _scorer           = scorer;
        _organizer        = organizer;
        _taggers          = taggers;
        _assetRepo        = assetRepo;
        _worker           = worker;
        _publisher        = publisher;
        _options          = options.Value;
        _logger           = logger;
        _claimRepo        = claimRepo;
        _canonicalRepo    = canonicalRepo;
        _pipeline         = pipeline;
        _identity         = identity;
        _chainFactory     = chainFactory;
        _reviewRepo       = reviewRepo;
        _activityRepo     = activityRepo;
        _reconciliation   = reconciliation;
        _heroGenerator    = heroGenerator;
        _gate             = gate;
        _ingestionLog     = ingestionLog;
        _smartLabeler      = smartLabeler;
        _typeAdvisor       = typeAdvisor;
        _timelineRepo      = timelineRepo;
        _scoringConfig     = scoringConfig;
        _batchRepo         = batchRepo;
        _imagePathService  = imagePathService;
    }

    // =========================================================================
    // BackgroundService
    // =========================================================================

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Always wire the event handler so hot-swap from PUT /settings/folders
        // works even if no directory was configured at startup.
        _watcher.FileDetected += (_, evt) =>
        {
            BufferFswEvent(evt);
        };

        // ── Step 1: Log server start (no paths — just the fact) ──────────
        _logger.LogInformation("IngestionEngine started");
        await SafeActivityLogAsync(new Domain.Entities.SystemActivityEntry
        {
            ActionType = Domain.Enums.SystemActionType.ServerStarted,
            EntityType = "Server",
            Detail     = "Server started",
        }, stoppingToken).ConfigureAwait(false);

        // ── Step 2: Reconcile BEFORE scanning ────────────────────────────
        // Clean orphaned DB records so the initial scan sees files as fresh
        // rather than producing false "duplicate skipped" entries.
        try
        {
            _logger.LogInformation("Running startup reconciliation...");
            var result = await _reconciliation.ReconcileAsync(stoppingToken).ConfigureAwait(false);
            _logger.LogInformation(
                "Startup reconciliation complete: {Total} scanned, {Missing} missing",
                result.TotalScanned, result.MissingCount);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Startup reconciliation failed — continuing with scan");
        }

        // ── Step 2b: Migrate .orphans → .data/staging ──────────────────────────
        // One-time migration: if the legacy .orphans directory exists and .data/staging
        // does not, rename it. Also update DB records that reference the old path.
        await MigrateOrphansToStagingAsync(stoppingToken).ConfigureAwait(false);

        // ── Step 3: Start watching + initial scan ────────────────────────
        if (!string.IsNullOrWhiteSpace(_options.WatchDirectory))
        {
            try
            {
                _watcher.AddDirectory(_options.WatchDirectory, _options.IncludeSubdirectories);
                _logger.LogInformation("Watching: {Path}", _options.WatchDirectory);
            }
            catch (DirectoryNotFoundException)
            {
                _logger.LogWarning(
                    "IngestionEngine: Watch directory does not exist: {Path}. " +
                    "Create the directory or update the path in Settings.",
                    _options.WatchDirectory);
            }
        }
        else
        {
            _logger.LogInformation(
                "IngestionEngine: No WatchDirectory configured. " +
                "Set a Watch Folder in Settings to begin file ingestion.");
        }

        // Mark the watcher as "running" so that a later UpdateDirectory() call
        // (from PUT /settings/folders) immediately activates the new watcher.
        // Safe to call with zero directories — Start() is a no-op on an empty list.
        _watcher.Start();

        // Initial scan: FileSystemWatcher only detects NEW filesystem events — files
        // that are already present in the Watch Folder at startup are invisible to it.
        // Synthesise "Created" events for every existing file so the pipeline processes
        // them through the normal debounce → hash → duplicate-check → process flow.
        // After reconciliation, orphaned records are cleaned, so files in the
        // watch folder are treated as genuinely new — no false duplicate skips.
        if (!string.IsNullOrWhiteSpace(_options.WatchDirectory)
            && Directory.Exists(_options.WatchDirectory))
        {
            ScanExistingFiles(_options.WatchDirectory, _options.IncludeSubdirectories);
        }

        // Start the polling fallback in the background.
        // FileSystemWatcher can miss events on certain configurations — the poller
        // periodically sweeps the Watch Folder and synthesises Created events for
        // files that the debounce queue hasn't seen yet.
        if (_options.PollIntervalSeconds > 0)
            _ = PollWatchDirectoryAsync(stoppingToken);

        // Consume candidates until the service is stopped.
        // If no watcher is active yet, this loop simply waits — new events will
        // flow once the user sets a Watch Folder via the Settings page.
        await foreach (var candidate in _debounce.Reader.ReadAllAsync(stoppingToken)
                           .ConfigureAwait(false))
        {
            // Enqueue each candidate as an independent pipeline task.
            await _worker.EnqueueAsync(
                candidate,
                (c, ct) => ProcessCandidateAsync(c, ct),
                stoppingToken).ConfigureAwait(false);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _watcher.Stop();
        _debounce.Complete();
        await _worker.DrainAsync(cancellationToken).ConfigureAwait(false);

        await SafeActivityLogAsync(new Domain.Entities.SystemActivityEntry
        {
            ActionType = Domain.Enums.SystemActionType.ServerStopped,
            EntityType = "Server",
            Detail     = "Ingestion engine stopped.",
        }, cancellationToken).ConfigureAwait(false);

        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("IngestionEngine stopped.");
    }

    // =========================================================================
    // IIngestionEngine — explicit interface (Start/StopAsync are the public API)
    // =========================================================================

    /// <inheritdoc/>
    void IIngestionEngine.Start()
    {
        // BackgroundService is started by the host; this satisfies the interface
        // for callers that hold an IIngestionEngine reference.
        _watcher.Start();

        // Re-scan the watch directory for files that were already present before
        // the watcher started.  This covers the post-wipe restart scenario where
        // files are seeded into the watch folder and then the engine is restarted.
        if (!string.IsNullOrWhiteSpace(_options.WatchDirectory)
            && Directory.Exists(_options.WatchDirectory))
        {
            ScanExistingFiles(_options.WatchDirectory, _options.IncludeSubdirectories);
        }
    }

    /// <inheritdoc/>
    async Task IIngestionEngine.StopAsync(CancellationToken ct)
        => await StopAsync(ct).ConfigureAwait(false);

    /// <inheritdoc/>
    void IIngestionEngine.ScanDirectory(string directory, bool includeSubdirectories)
        => ScanExistingFiles(directory, includeSubdirectories);

    /// <inheritdoc/>
    void IIngestionEngine.PauseWatcher()
    {
        // Stop the FSW so no new OS events are delivered while the wipe runs.
        _watcher.Stop();

        // Discard every pending FSW event and clear the dedup sets so that
        // files written after the wipe are not silently dropped as duplicates.
        // The debounce channel and its consumer loop are deliberately left alive.
        lock (_fswBufferLock)
        {
            _fswFlushTimer?.Dispose();
            _fswFlushTimer = null;
            _fswBuffer.Clear();
            _fswBufferedPaths.Clear();
            _enqueuedPaths.Clear();
        }

        _logger.LogInformation("IngestionEngine: FSW paused (watcher stopped, event buffer cleared).");
    }

    /// <inheritdoc/>
    void IIngestionEngine.ResumeWatcher()
    {
        // Clear dedup state so files that were seen before the wipe (and whose
        // paths are now back on disk after re-seeding) can be enqueued again.
        lock (_fswBufferLock)
        {
            _enqueuedPaths.Clear();
            _fswBufferedPaths.Clear();
        }

        // Restart the FSW — new OS events will flow into BufferFswEvent again.
        _watcher.Start();

        _logger.LogInformation("IngestionEngine: FSW resumed (watcher restarted, dedup state cleared).");
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PendingOperation>> DryRunAsync(
        string rootPath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        var operations = new List<PendingOperation>();

        var files = Directory.EnumerateFiles(rootPath, "*.*", SearchOption.AllDirectories);

        foreach (var filePath in files)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var ops = await SimulateFileAsync(filePath, ct).ConfigureAwait(false);
                operations.AddRange(ops);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "DryRun: error simulating {Path}", filePath);
            }
        }

        return operations;
    }

    // =========================================================================
    // Live pipeline
    // =========================================================================

    private async Task ProcessCandidateAsync(IngestionCandidate candidate, CancellationToken ct)
    {
        // Serialize processing for files in the same source folder.
        // This ensures: (a) duplicate hash detection works when byte-identical
        // files arrive concurrently, and (b) folder hints from the first file
        // are available when sibling files start processing.
        var folderKey = (Path.GetDirectoryName(candidate.Path) ?? string.Empty)
            .Replace('\\', '/').TrimEnd('/');
        var folderLock = _concurrencyGuard.GetFolderLock(folderKey);
        await folderLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
        await ProcessCandidateCoreAsync(candidate, ct).ConfigureAwait(false);
        }
        finally
        {
            folderLock.Release();
        }
    }

    private async Task ProcessCandidateCoreAsync(IngestionCandidate candidate, CancellationToken ct)
    {
        // Use the batch ID as the ingestion run ID so all activity entries
        // for files in the same batch share one correlation ID.
        var ingestionRunId = candidate.BatchId ?? Guid.NewGuid();

        // Step 2: skip failed probe candidates.
        if (candidate.IsFailed)
        {
            _logger.LogWarning(
                "Ingestion skipped (lock probe failed): {Path} — {Reason}",
                candidate.Path, candidate.FailureReason);
            await SafePublishAsync(SignalREvents.IngestionFailed, new IngestionFailedEvent(
                candidate.Path,
                candidate.FailureReason ?? "Lock probe exhausted",
                DateTimeOffset.UtcNow), ct).ConfigureAwait(false);
            return;
        }

        // Step 3: handle deletion.
        if (candidate.EventType == FileEventType.Deleted)
        {
            await HandleDeletedAsync(candidate, ct).ConfigureAwait(false);
            return;
        }

        if (!File.Exists(candidate.Path))
        {
            _logger.LogWarning("Ingestion skipped — file missing: {Path}", candidate.Path);
            return;
        }

        await SafePublishAsync(SignalREvents.IngestionStarted, new IngestionStartedEvent(
            candidate.Path, DateTimeOffset.UtcNow), ct).ConfigureAwait(false);

        // Lifecycle log: create entry at detection.
        var logEntryId = Guid.NewGuid();
        try
        {
            await _ingestionLog.InsertAsync(new Domain.Entities.IngestionLogEntry
            {
                Id             = logEntryId,
                FilePath       = candidate.Path,
                Status         = "detected",
                IngestionRunId = ingestionRunId,
            }, ct).ConfigureAwait(false);
        }
        catch (Exception ex) { _logger.LogDebug(ex, "Ingestion log insert failed — continuing"); }

        var pipelineStopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Step 4: hash.
        var hash = await _hasher.ComputeAsync(candidate.Path, ct).ConfigureAwait(false);

        await SafePublishAsync(SignalREvents.IngestionHashed, new IngestionHashedEvent(
            candidate.Path, hash.Hex, hash.FileSize, hash.Elapsed), ct).ConfigureAwait(false);

        await SafeActivityLogAsync(new Domain.Entities.SystemActivityEntry
        {
            ActionType     = Domain.Enums.SystemActionType.FileHashed,
            EntityType     = "MediaAsset",
            Detail         = $"Fingerprinted: {hash.Hex[..12]}… ({hash.FileSize / 1024.0:F1} KB)",
            ChangesJson    = JsonSerializer.Serialize(new
            {
                hash_prefix  = hash.Hex[..12],
                full_hash    = hash.Hex,
                file_size_kb = Math.Round(hash.FileSize / 1024.0, 1),
                elapsed_ms   = (long)hash.Elapsed.TotalMilliseconds,
                filename     = Path.GetFileName(candidate.Path),
            }),
            IngestionRunId = ingestionRunId,
        }, ct).ConfigureAwait(false);

        // Lifecycle log: hashing complete.
        try { await _ingestionLog.UpdateStatusAsync(logEntryId, "hashing", contentHash: hash.Hex, ct: ct).ConfigureAwait(false); }
        catch (Exception ex) { _logger.LogDebug(ex, "Ingestion log update failed — continuing"); }

        // Step 5: duplicate check.
        // If the file is already ingested but still sitting in the Watch Folder
        // (e.g. it scored below the confidence gate on first pass, or LibraryRoot
        // was not configured at that time), attempt to organize it now — metadata
        // may have been enriched by external providers since the initial scan.
        // Hash lock scope: covers duplicate check → DB insert → organization.
        // Prevents race conditions when identical files arrive simultaneously.
        // Defense-in-depth: DB uses INSERT OR IGNORE on content_hash UNIQUE constraint.
        var hashLock = _concurrencyGuard.GetHashLock(hash.Hex);
        await hashLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
        var existing = await _assetRepo.FindByHashAsync(hash.Hex, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            // If the original file no longer exists, clean up the orphaned record
            // and all associated filesystem artifacts, then fall through to process
            // this file as a brand new asset.
            if (!File.Exists(existing.FilePathRoot))
            {
                await CleanStagedAssetAsync(existing, ct).ConfigureAwait(false);
                // Fall through — process as new asset below.
            }
            else
            {
                // Same-path re-detection (polling fallback or FSW echo): the file
                // is already tracked and still at its original location.  Attempt
                // re-organization silently — only log DuplicateSkipped when the
                // candidate is a genuinely different file at a new path.
                bool isSamePath = string.Equals(
                    Path.GetFullPath(candidate.Path),
                    Path.GetFullPath(existing.FilePathRoot),
                    StringComparison.OrdinalIgnoreCase);

                if (!isSamePath)
                {
                    // True duplicate: a different source file has the same content hash
                    // and the original is still on disk. Log it, delete the duplicate
                    // from the watch folder, and return — do NOT move it into the library.
                    _logger.LogInformation(
                        "True duplicate detected: {CandidatePath} has same hash as existing {ExistingPath} — deleting duplicate from watch folder",
                        candidate.Path, existing.FilePathRoot);

                    await SafeActivityLogAsync(new Domain.Entities.SystemActivityEntry
                    {
                        ActionType     = Domain.Enums.SystemActionType.DuplicateSkipped,
                        EntityId       = existing.Id,
                        EntityType     = "MediaAsset",
                        Detail         = $"Duplicate skipped and deleted: {Path.GetFileName(candidate.Path)} (identical to {Path.GetFileName(existing.FilePathRoot)})",
                        IngestionRunId = ingestionRunId,
                    }, ct).ConfigureAwait(false);

                    try
                    {
                        if (File.Exists(candidate.Path))
                            File.Delete(candidate.Path);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "Could not delete duplicate file {Path}", candidate.Path);
                    }

                    return;
                }

                // Same-path re-detection: attempt re-organization (file may have been
                // enriched since first scan).
                await TryReorganizeExistingAsync(existing, candidate.Path, ct)
                    .ConfigureAwait(false);
                return;
            }
        }

        // Step 6: process.
        var result = await _processors.ProcessAsync(candidate.Path, ct).ConfigureAwait(false);

        await SafeActivityLogAsync(new Domain.Entities.SystemActivityEntry
        {
            ActionType     = Domain.Enums.SystemActionType.FileProcessed,
            EntityType     = "MediaAsset",
            Detail         = $"Scanned: {result.DetectedType} — {result.Claims.Count} fields, cover {(result.CoverImage?.Length > 0 ? "found" : "absent")}",
            ChangesJson    = JsonSerializer.Serialize(new
            {
                detected_type  = result.DetectedType.ToString(),
                claims_count   = result.Claims.Count,
                has_cover      = result.CoverImage?.Length > 0,
                cover_bytes    = result.CoverImage?.Length ?? 0,
                is_corrupt     = result.IsCorrupt,
                corrupt_reason = result.CorruptReason,
                filename       = Path.GetFileName(candidate.Path),
            }),
            IngestionRunId = ingestionRunId,
        }, ct).ConfigureAwait(false);

        // Step 6b: AI Smart Labeling — enhance title claim using LLM.
        // The SmartLabeler understands context that regex cannot:
        // "2001 A Space Odyssey" keeps the year, "Frank Herbert - Dune" extracts the author.
        {
            var rawTitle = result.Claims.FirstOrDefault(c =>
                c.Key.Equals(MetadataFieldConstants.Title, StringComparison.OrdinalIgnoreCase));

            if (rawTitle is not null)
            {
                try
                {
                    var cleaned = await _smartLabeler.CleanAsync(
                        Path.GetFileNameWithoutExtension(candidate.Path), ct).ConfigureAwait(false);

                    if (!string.IsNullOrWhiteSpace(cleaned.Title) && cleaned.Confidence > 0.5)
                    {
                        // Replace the title claim with the AI-cleaned version at higher confidence.
                        var updatedClaims = result.Claims
                            .Where(c => !c.Key.Equals(MetadataFieldConstants.Title, StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        updatedClaims.Add(new Processors.Models.ExtractedClaim
                        {
                            Key = MetadataFieldConstants.Title,
                            Value = cleaned.Title,
                            Confidence = Math.Max(rawTitle.Confidence, cleaned.Confidence),
                        });

                        // Add author if extracted and not already present.
                        if (!string.IsNullOrWhiteSpace(cleaned.Author)
                            && !result.Claims.Any(c => c.Key.Equals(MetadataFieldConstants.Author, StringComparison.OrdinalIgnoreCase)))
                        {
                            updatedClaims.Add(new Processors.Models.ExtractedClaim
                            {
                                Key = MetadataFieldConstants.Author,
                                Value = cleaned.Author,
                                Confidence = cleaned.Confidence * 0.9,
                            });
                        }

                        // Add year if extracted and not already present.
                        if (cleaned.Year.HasValue
                            && !result.Claims.Any(c => c.Key.Equals(MetadataFieldConstants.Year, StringComparison.OrdinalIgnoreCase)
                                                    || c.Key.Equals("release_year", StringComparison.OrdinalIgnoreCase)))
                        {
                            updatedClaims.Add(new Processors.Models.ExtractedClaim
                            {
                                Key = "release_year",
                                Value = cleaned.Year.Value.ToString(),
                                Confidence = cleaned.Confidence * 0.85,
                            });
                        }

                        // Add season number if extracted and not already present.
                        if (cleaned.Season.HasValue
                            && !updatedClaims.Any(c => c.Key.Equals(MetadataFieldConstants.SeasonNumber, StringComparison.OrdinalIgnoreCase)))
                        {
                            updatedClaims.Add(new Processors.Models.ExtractedClaim
                            {
                                Key = MetadataFieldConstants.SeasonNumber,
                                Value = cleaned.Season.Value.ToString(),
                                Confidence = cleaned.Confidence,
                            });
                        }

                        // Add episode number if extracted and not already present.
                        if (cleaned.Episode.HasValue
                            && !updatedClaims.Any(c => c.Key.Equals(MetadataFieldConstants.EpisodeNumber, StringComparison.OrdinalIgnoreCase)))
                        {
                            updatedClaims.Add(new Processors.Models.ExtractedClaim
                            {
                                Key = MetadataFieldConstants.EpisodeNumber,
                                Value = cleaned.Episode.Value.ToString(),
                                Confidence = cleaned.Confidence,
                            });
                        }

                        result = new Processors.Models.ProcessorResult
                        {
                            FilePath           = result.FilePath,
                            DetectedType       = result.DetectedType,
                            Claims             = updatedClaims,
                            CoverImage         = result.CoverImage,
                            CoverImageMimeType = result.CoverImageMimeType,
                            IsCorrupt          = result.IsCorrupt,
                            CorruptReason      = result.CorruptReason,
                            MediaTypeCandidates = result.MediaTypeCandidates,
                        };

                        _logger.LogInformation(
                            "SmartLabeler enhanced title: \"{OldTitle}\" → \"{NewTitle}\" (confidence: {Conf:F2})",
                            rawTitle.Value, cleaned.Title, cleaned.Confidence);
                    }
                }
                catch (Exception ex)
                {
                    // SmartLabeler failure is non-fatal — processor claims stand.
                    _logger.LogWarning(ex, "SmartLabeler failed for {Path} — using processor title", candidate.Path);
                }
            }
        }

        // Step 7: quarantine corrupt files.
        if (result.IsCorrupt)
        {
            _logger.LogWarning("Corrupt file quarantined: {Path} — {Reason}",
                candidate.Path, result.CorruptReason);

            // Activity: media failed (replaces granular FileQuarantined).
            var failedJson = JsonSerializer.Serialize(new
            {
                source_file = Path.GetFileName(candidate.Path),
                reason      = result.CorruptReason,
                error_type  = "corrupt_file",
            });
            await SafeActivityLogAsync(new Domain.Entities.SystemActivityEntry
            {
                ActionType     = Domain.Enums.SystemActionType.MediaFailed,
                EntityType     = "MediaAsset",
                ChangesJson    = failedJson,
                Detail         = $"Failed — {Path.GetFileName(candidate.Path)}: {result.CorruptReason}",
                IngestionRunId = ingestionRunId,
            }, ct).ConfigureAwait(false);

            await SafePublishAsync(SignalREvents.IngestionFailed, new IngestionFailedEvent(
                candidate.Path,
                $"Corrupt: {result.CorruptReason}",
                DateTimeOffset.UtcNow), ct).ConfigureAwait(false);
            return;
        }

        // Step 8: convert claims.
        var assetId = Guid.NewGuid();
        var claims  = BuildClaims(assetId, result);

        // History: file detected.
        await SafeActivityLogAsync(new SystemActivityEntry
        {
            ActionType = SystemActionType.FileDetected,
            EntityId = assetId,
            Detail = $"File detected in watch folder: {Path.GetFileName(candidate.Path)}",
            IngestionRunId = ingestionRunId,
        }, ct).ConfigureAwait(false);

        // History: metadata extracted.
        await SafeActivityLogAsync(new SystemActivityEntry
        {
            ActionType = "MetadataExtracted",
            EntityId = assetId,
            Detail = $"Metadata extracted — {result.Claims.Count} fields found",
            IngestionRunId = ingestionRunId,
        }, ct).ConfigureAwait(false);

        // ── Timeline: Stage 0 file_scanned event ────────────────────────────
        try
        {
            await _timelineRepo.InsertEventAsync(new EntityEvent
            {
                EntityId       = assetId,
                EntityType     = "Work",
                EventType      = "file_scanned",
                Stage          = 0,
                Trigger        = "ingestion",
                ProviderId     = LocalProcessorProviderId.ToString(),
                ProviderName   = "local_processor",
                Confidence     = result.Claims.Count > 0 ? 1.0 : 0.0,
                IngestionRunId = ingestionRunId,
                Detail         = $"File scanned: {Path.GetFileName(candidate.Path)} — {result.Claims.Count} fields extracted ({result.DetectedType})",
            }, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to write Stage 0 timeline event for asset {Id}", assetId);
        }

        // Step 9: score.
        // CategoryConfidencePrior: currently 0.0 (single WatchDirectory = general catch-all).
        // When Library Folders (config/libraries.json) are implemented, category-specific
        // folders will set +0.10 and multi-type folders +0.05.
        var scoringContext = new ScoringContext
        {
            EntityId                = assetId,
            Claims                  = claims,
            ProviderWeights         = new Dictionary<Guid, double>
                { [LocalProcessorProviderId] = 1.0 },
            Configuration           = new ScoringConfiguration(),
            CategoryConfidencePrior = candidate.CategoryConfidencePrior,
            DetectedMediaType       = result.DetectedType,
        };

        var scored = await _scorer.ScoreEntityAsync(scoringContext, ct).ConfigureAwait(false);

        // History: confidence scored.
        await SafeActivityLogAsync(new SystemActivityEntry
        {
            ActionType = "ConfidenceScored",
            EntityId = assetId,
            Detail = $"Confidence: {scored.OverallConfidence:P0} — Score: {scored.OverallConfidence:F2}",
            IngestionRunId = ingestionRunId,
        }, ct).ConfigureAwait(false);

        // Phase 9: persist claims (append-only; enables re-scoring on weight changes).
        await _claimRepo.InsertBatchAsync(claims, ct).ConfigureAwait(false);

        await SafeActivityLogAsync(new Domain.Entities.SystemActivityEntry
        {
            ActionType     = Domain.Enums.SystemActionType.FileScored,
            EntityId       = assetId,
            EntityType     = "MediaAsset",
            Detail         = $"Score: {scored.OverallConfidence:P0} across {scored.FieldScores.Count} fields",
            ChangesJson    = JsonSerializer.Serialize(new
            {
                confidence    = scored.OverallConfidence,
                field_count   = scored.FieldScores.Count,
                conflicted    = scored.FieldScores.Count(f => f.IsConflicted),
                fields        = scored.FieldScores
                    .Where(f => !string.IsNullOrEmpty(f.WinningValue))
                    .Select(f => new { field = f.Key, value = f.WinningValue, confidence = f.Confidence, conflicted = f.IsConflicted })
                    .ToList(),
            }),
            IngestionRunId = ingestionRunId,
        }, ct).ConfigureAwait(false);

        // Lifecycle log: scored.
        var detectedTitle = scored.FieldScores
            .FirstOrDefault(f => f.Key.Equals(MetadataFieldConstants.Title, StringComparison.OrdinalIgnoreCase))?.WinningValue;
        try { await _ingestionLog.UpdateStatusAsync(logEntryId, "scored",
            confidenceScore: scored.OverallConfidence,
            detectedTitle: detectedTitle,
            ct: ct).ConfigureAwait(false); }
        catch (Exception ex) { _logger.LogDebug(ex, "Ingestion log update failed — continuing"); }

        // Phase 9: persist canonical values (current winning metadata for this asset).
        // Phase B: also persist the IsConflicted flag from the scoring engine so
        // the Dashboard can surface unresolved metadata disagreements.
        var canonicals = scored.FieldScores
            .Where(f => !string.IsNullOrEmpty(f.WinningValue))
            .Select(f => new CanonicalValue
            {
                EntityId     = assetId,
                Key          = f.Key,
                Value        = f.WinningValue!,
                LastScoredAt = scored.ScoredAt,
                IsConflicted = f.IsConflicted,
            })
            .ToList();

        // Step 6a: media type resolution from processor candidates.
        // When a processor emits MediaTypeCandidates (ambiguous formats like MP3, MP4),
        // the top candidate's confidence determines auto-assign vs review queue.
        var resolvedMediaType = result.DetectedType;
        bool mediaTypeIsConflicted = false;
        bool mediaTypeNeedsReview  = false;

        // Library folder prior: if the file comes from a configured library folder
        // (config/libraries.json), use that folder's media_types as a strong prior.
        // This prevents MP3 files in a Books/Audiobook folder from being classified
        // as Music when the processor heuristics are ambiguous.
        var candidateList = result.MediaTypeCandidates.ToList();
        if (_options.LibraryFolders.Count > 0 && (candidateList.Count > 0 || resolvedMediaType == MediaType.Unknown))
        {
            var matchedFolder = _options.LibraryFolders.FirstOrDefault(f =>
                !string.IsNullOrWhiteSpace(f.SourcePath) &&
                candidate.Path.StartsWith(f.SourcePath, StringComparison.OrdinalIgnoreCase));

            if (matchedFolder is not null && matchedFolder.MediaTypes.Count > 0)
            {
                var folderTypes = matchedFolder.MediaTypes;

                // Find the processor's top candidate that is also in the folder's configured types.
                var matchingCandidate = candidateList.FirstOrDefault(c => folderTypes.Contains(c.Type));

                if (matchingCandidate is not null)
                {
                    // The processor agrees with the folder's configured type(s) — boost confidence.
                    // Must exceed the top candidate's confidence to win the sort, and be at least 0.98
                    // (library folder config is authoritative when a matching type exists).
                    var topConfidence = candidateList.Max(c => c.Confidence);
                    var boostedConfidence = Math.Max(Math.Max(topConfidence + 0.01, matchingCandidate.Confidence), 0.98);
                    var index = candidateList.IndexOf(matchingCandidate);
                    candidateList[index] = new Domain.Models.MediaTypeCandidate
                    {
                        Type       = matchingCandidate.Type,
                        Confidence = boostedConfidence,
                        Reason     = matchingCandidate.Reason,
                    };

                    // Re-sort so the boosted candidate is first.
                    candidateList = [.. candidateList.OrderByDescending(c => c.Confidence)];

                    _logger.LogInformation(
                        "Library folder prior applied: boosted {Type} to {Confidence:P0} for {Path} (folder configured for [{Types}])",
                        matchingCandidate.Type, boostedConfidence, candidate.Path,
                        string.Join(", ", folderTypes));
                }
                else if (folderTypes.Count == 1)
                {
                    // Processor did not produce a candidate matching the folder's single type.
                    // Override to the folder's type at high confidence — the folder config is authoritative.
                    candidateList.Insert(0, new Domain.Models.MediaTypeCandidate
                    {
                        Type       = folderTypes[0],
                        Confidence = 0.95,
                        Reason     = $"Library folder configured for {folderTypes[0]}",
                    });

                    _logger.LogInformation(
                        "Library folder prior override: assigned {Type} at 0.95 for {Path} (processor top was {ProcessorTop}; folder only allows {Type})",
                        folderTypes[0], candidate.Path,
                        candidateList.Count > 1 ? candidateList[1].Type.ToString() : "none",
                        folderTypes[0]);
                }
                else if (candidateList.Count == 0)
                {
                    // Processor returned no candidates and the folder has multiple types.
                    // Add all folder types as candidates at moderate confidence so the
                    // item is classified rather than staying Unknown.
                    foreach (var ft in folderTypes)
                    {
                        candidateList.Add(new Domain.Models.MediaTypeCandidate
                        {
                            Type       = ft,
                            Confidence = 0.65,
                            Reason     = $"Library folder configured for {ft} (multi-type folder, needs disambiguation)",
                        });
                    }

                    _logger.LogInformation(
                        "Library folder prior: injected {Count} candidates from multi-type folder [{Types}] for {Path}",
                        folderTypes.Count, string.Join(", ", folderTypes), candidate.Path);
                }
                else
                {
                    // Processor top candidate is not in the folder's multi-type list.
                    // Keep processor result but log the mismatch for diagnostics.
                    _logger.LogInformation(
                        "Library folder prior: processor top {ProcessorTop} not in folder types [{Types}] for {Path} — no override applied",
                        candidateList[0].Type, string.Join(", ", folderTypes), candidate.Path);
                }
            }
        }

        // Step 6a-Root: When a file arrives in the root of a watch directory (not a
        // typed subfolder), ambiguous extensions cannot be reliably classified.
        // Cap all candidate confidences at 0.40 so the file is always sent to review,
        // giving the user the chance to confirm the media type.
        // Unambiguous extensions (.epub, .cbz, .cbr, .m4b, .pdf) are excluded — they
        // can only ever be one media type and do not need the cap.
        if (!string.IsNullOrWhiteSpace(_options.WatchDirectory))
        {
            var fileDir  = Path.GetDirectoryName(candidate.Path);
            var watchDir = _options.WatchDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            bool isRootDrop = string.Equals(fileDir?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                                            watchDir,
                                            StringComparison.OrdinalIgnoreCase);

            if (isRootDrop)
            {
                var ext = Path.GetExtension(candidate.Path)?.ToLowerInvariant();
                var unambiguousExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    { ".epub", ".cbz", ".cbr", ".m4b", ".pdf" };

                if (!unambiguousExtensions.Contains(ext ?? ""))
                {
                    // Cap every candidate confidence at 0.40 to force the review queue.
                    const double RootWatchFolderMaxConfidence = 0.40;
                    for (int i = 0; i < candidateList.Count; i++)
                    {
                        if (candidateList[i].Confidence > RootWatchFolderMaxConfidence)
                        {
                            candidateList[i] = new Domain.Models.MediaTypeCandidate
                            {
                                Type       = candidateList[i].Type,
                                Confidence = RootWatchFolderMaxConfidence,
                                Reason     = $"Confidence capped at {RootWatchFolderMaxConfidence} — root watch folder drop (ambiguous extension {ext})",
                            };
                        }
                    }

                    // Also cap the candidate's additive confidence prior so the scoring engine
                    // cannot boost the file past review threshold.
                    candidate.CategoryConfidencePrior = 0.0;

                    _logger.LogInformation(
                        "Root watch folder drop detected for {Path} (ext: {Ext}) — confidence capped at {Cap:P0}, routed to review",
                        candidate.Path, ext, RootWatchFolderMaxConfidence);
                }
            }
        }

        // Step 6a-AI: Call the AI MediaTypeAdvisor when the processor could not determine
        // the media type. This covers two cases:
        //   1. Processor returned Unknown + empty candidates (ambiguous format like MP3, MP4,
        //      M4A, MKV, AVI — the processor no longer runs heuristics for these).
        //   2. Processor returned candidates but top confidence is below the auto-assign
        //      threshold (legacy path — shouldn't occur after heuristic removal, kept for safety).
        bool advisorNeeded = result.DetectedType == MediaType.Unknown && candidateList.Count == 0;
        bool advisorLowConfidence = candidateList.Count > 0 && candidateList[0].Confidence < _options.MediaTypeAutoAssignThreshold;

        if (advisorNeeded || advisorLowConfidence)
        {
            try
            {
                var genreClaim = result.Claims.FirstOrDefault(c =>
                    c.Key.Equals(MetadataFieldConstants.Genre, StringComparison.OrdinalIgnoreCase));
                var durationClaim = result.Claims.FirstOrDefault(c =>
                    c.Key.Equals("duration_sec", StringComparison.OrdinalIgnoreCase));
                var bitrateClaim = result.Claims.FirstOrDefault(c =>
                    c.Key.Equals("audio_bitrate", StringComparison.OrdinalIgnoreCase));
                var containerClaim = result.Claims.FirstOrDefault(c =>
                    c.Key.Equals("container", StringComparison.OrdinalIgnoreCase));

                double? duration = durationClaim is not null && double.TryParse(durationClaim.Value, out var d) ? d : null;
                int? bitrate = bitrateClaim is not null && int.TryParse(bitrateClaim.Value, out var b) ? b : null;

                var aiCandidate = await _typeAdvisor.ClassifyAsync(
                    Path.GetFileName(candidate.Path),
                    containerClaim?.Value,
                    duration,
                    bitrate,
                    genreClaim?.Value,
                    result.Claims.Any(c => c.Key.Equals("chapter_count", StringComparison.OrdinalIgnoreCase)),
                    Path.GetDirectoryName(candidate.Path),
                    ct).ConfigureAwait(false);

                if (aiCandidate.Type != MediaType.Unknown)
                {
                    if (advisorNeeded)
                    {
                        // No processor candidates at all — AI result is the only classification.
                        candidateList.Insert(0, aiCandidate);
                        _logger.LogInformation(
                            "MediaTypeAdvisor classified {Path} as {Type} ({Conf:F2}) (processor returned Unknown)",
                            candidate.Path, aiCandidate.Type, aiCandidate.Confidence);
                    }
                    else if (aiCandidate.Confidence > candidateList[0].Confidence)
                    {
                        // AI is more confident than the processor's top candidate — promote it.
                        candidateList.Insert(0, aiCandidate);
                        _logger.LogInformation(
                            "MediaTypeAdvisor classified {Path} as {Type} ({Conf:F2}), overriding processor top {OldType} ({OldConf:F2})",
                            candidate.Path, aiCandidate.Type, aiCandidate.Confidence,
                            candidateList[1].Type, candidateList[1].Confidence);
                    }
                }
            }
            catch (Exception ex)
            {
                // AI classification failure is non-fatal — processor candidates stand (or remain empty).
                _logger.LogWarning(ex, "MediaTypeAdvisor failed for {Path} — using processor classification", candidate.Path);
            }
        }

        if (candidateList.Count > 0)
        {
            var topCandidate = candidateList[0];

            if (topCandidate.Confidence >= _options.MediaTypeAutoAssignThreshold)
            {
                // High confidence — accept automatically.
                resolvedMediaType = topCandidate.Type;
                _logger.LogInformation(
                    "Media type auto-assigned: {Type} ({Confidence:P0}) for {Path}",
                    topCandidate.Type, topCandidate.Confidence, candidate.Path);
            }
            else if (topCandidate.Confidence >= _options.MediaTypeReviewThreshold)
            {
                // Medium confidence — accept provisionally, flag for review.
                resolvedMediaType     = topCandidate.Type;
                mediaTypeIsConflicted = true;
                mediaTypeNeedsReview  = true;
                _logger.LogInformation(
                    "Media type provisional: {Type} ({Confidence:P0}) for {Path} — flagged for review",
                    topCandidate.Type, topCandidate.Confidence, candidate.Path);
            }
            else
            {
                // Low confidence — assign Unknown, flag for review.
                resolvedMediaType     = MediaType.Unknown;
                mediaTypeIsConflicted = true;
                mediaTypeNeedsReview  = true;
                _logger.LogWarning(
                    "Media type ambiguous ({Confidence:P0}) for {Path} — assigned Unknown, flagged for review",
                    topCandidate.Confidence, candidate.Path);
            }
        }

        // Always persist the resolved media_type as a canonical value so that
        // TryReorganizeExistingAsync (and any future re-score) knows the file type.
        // Without this, re-organization from canonical values loses the media type
        // and defaults to "Other".
        canonicals.Add(new CanonicalValue
        {
            EntityId     = assetId,
            Key          = MetadataFieldConstants.MediaTypeField,
            Value        = resolvedMediaType.ToString(),
            LastScoredAt = scored.ScoredAt,
            IsConflicted = mediaTypeIsConflicted,
        });

        // Persist cover_url canonical value so the Registry listing query can show
        // cover art thumbnails. Without this, the sidebar shows "Missing Art" even
        // when cover.jpg exists on disk — the listing query reads from canonical_values,
        // not from the filesystem.
        if (result.CoverImage is { Length: > 0 })
        {
            canonicals.Add(new CanonicalValue
            {
                EntityId     = assetId,
                Key          = MetadataFieldConstants.CoverUrl,
                Value        = $"/stream/{assetId}/cover",
                LastScoredAt = scored.ScoredAt,
            });
        }

        await _canonicalRepo.UpsertBatchAsync(canonicals, ct).ConfigureAwait(false);

        // Create MetadataConflict review item when any canonical value has IsConflicted=true.
        // Conflicts don't block organization — the file proceeds with the best-guess value.
        var conflictedFields = canonicals
            .Where(c => c.IsConflicted && c.Key != MetadataFieldConstants.MediaTypeField) // media_type handled separately
            .Select(c => c.Key)
            .ToList();

        if (conflictedFields.Count > 0)
        {
            await CreateMetadataConflictReviewItemAsync(
                assetId,
                scored.OverallConfidence,
                conflictedFields,
                ct, ingestionRunId).ConfigureAwait(false);
        }

        // Create AmbiguousMediaType review item when media type confidence is below threshold.
        if (mediaTypeNeedsReview && candidateList.Count > 0)
        {
            var candidatesJson = JsonSerializer.Serialize(
                candidateList.Select(c => new
                {
                    type       = c.Type.ToString(),
                    confidence = c.Confidence,
                    reason     = c.Reason,
                }),
                new JsonSerializerOptions { WriteIndented = false });

            await CreateAmbiguousMediaTypeReviewItemAsync(
                assetId,
                candidateList[0].Confidence,
                candidatesJson,
                candidateList[0].Reason,
                ct, ingestionRunId).ConfigureAwait(false);
        }

        // Create RootWatchFolder review item when a file was dropped directly into the
        // root of a watch directory with an ambiguous extension (confidence was capped
        // to 0.40 in the root-folder detection step above).
        if (!string.IsNullOrWhiteSpace(_options.WatchDirectory))
        {
            var fileDir  = Path.GetDirectoryName(candidate.Path);
            var watchDir = _options.WatchDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            bool isRootDrop = string.Equals(
                fileDir?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                watchDir,
                StringComparison.OrdinalIgnoreCase);

            var ext = Path.GetExtension(candidate.Path)?.ToLowerInvariant();
            var unambiguousExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { ".epub", ".cbz", ".cbr", ".m4b", ".pdf" };

            if (isRootDrop && !unambiguousExts.Contains(ext ?? ""))
            {
                await CreateIngestionReviewItemAsync(
                    assetId,
                    ReviewTrigger.RootWatchFolder,
                    candidateList.Count > 0 ? candidateList[0].Confidence : 0.0,
                    $"File dropped into root watch folder — please confirm the media type (extension: {ext})",
                    ct, ingestionRunId).ConfigureAwait(false);
            }
        }

        // Enrich the candidate with resolved metadata.
        candidate.Metadata          = BuildMetadataDict(scored);
        candidate.DetectedMediaType = resolvedMediaType;

        // Step 9b: create Hub → Work → Edition chain so the FK on media_assets
        // can be satisfied.  The factory reuses an existing Hub when a matching
        // display name is found; otherwise it creates a fresh chain.
        var editionId = await _chainFactory.EnsureEntityChainAsync(
            resolvedMediaType,
            candidate.Metadata,
            ct).ConfigureAwait(false);

        await SafeActivityLogAsync(new Domain.Entities.SystemActivityEntry
        {
            ActionType     = Domain.Enums.SystemActionType.EntityChainCreated,
            EntityId       = assetId,
            EntityType     = "MediaAsset",
            Detail         = $"Catalogue entry created for \"{candidate.Metadata?.GetValueOrDefault(MetadataFieldConstants.Title, "Unknown") ?? "Unknown"}\"",
            ChangesJson    = JsonSerializer.Serialize(new
            {
                title      = candidate.Metadata?.GetValueOrDefault(MetadataFieldConstants.Title),
                author     = candidate.Metadata?.GetValueOrDefault(MetadataFieldConstants.Author),
                media_type = resolvedMediaType.ToString(),
                edition_id = editionId.ToString(),
            }),
            IngestionRunId = ingestionRunId,
        }, ct).ConfigureAwait(false);

        // Step 10: insert asset.
        var asset = new MediaAsset
        {
            Id           = assetId,
            EditionId    = editionId,
            ContentHash  = hash.Hex,
            FilePathRoot = candidate.Path,
            Status       = AssetStatus.Normal,
        };

        bool inserted = await _assetRepo.InsertAsync(asset, ct).ConfigureAwait(false);
        if (!inserted)
        {
            // Race: another thread inserted the same hash concurrently.
            _logger.LogDebug("Asset already inserted by concurrent task: {Hash}", hash.Hex[..12]);
            return;
        }

        var resolvedTitle  = candidate.Metadata?.GetValueOrDefault(MetadataFieldConstants.Title,  "Unknown") ?? "Unknown";
        var resolvedAuthor = candidate.Metadata?.GetValueOrDefault(MetadataFieldConstants.Author, string.Empty) ?? string.Empty;

        _logger.LogInformation(
            "Ingested [{Type}] '{Title}' (confidence={Confidence:P0}, hash={Hash})",
            resolvedMediaType, resolvedTitle, scored.OverallConfidence, hash.Hex[..12]);

        // Log to activity ledger so the Activity tab shows what was ingested and matched.
        string authorPart = string.IsNullOrWhiteSpace(resolvedAuthor) ? string.Empty : $" by {resolvedAuthor}";

        // Build structured JSON for the rich match card in the Dashboard.
        var resolvedYear        = candidate.Metadata?.GetValueOrDefault(MetadataFieldConstants.Year, string.Empty) ?? string.Empty;
        var resolvedDescription = candidate.Metadata?.GetValueOrDefault(MetadataFieldConstants.Description, string.Empty) ?? string.Empty;
        var richJson = JsonSerializer.Serialize(new
        {
            title       = resolvedTitle,
            author      = resolvedAuthor,
            year        = resolvedYear,
            media_type  = resolvedMediaType.ToString(),
            confidence  = scored.OverallConfidence,
            source_file = Path.GetFileName(candidate.Path),
            description = resolvedDescription,
            entity_id   = assetId.ToString(),
        });

        // Demoted from activity ledger to debug log (Phase 5 — activity consolidation).
        // The consolidated MediaAdded event is created at the end of the hydration pipeline.
        _logger.LogDebug(
            "FileDetected — \"{Title}\"{Author} ({Confidence:P0})",
            resolvedTitle, authorPart, scored.OverallConfidence);

        await SafePublishAsync(SignalREvents.IngestionCompleted, new IngestionCompletedEvent(
            candidate.Path,
            resolvedMediaType.ToString(),
            DateTimeOffset.UtcNow), ct).ConfigureAwait(false);

        // Emit incremental ingestion progress so the Dashboard can show a live counter.
        // Fetch the batch total from the repository; fall back to 0 if not available.
        if (candidate.BatchId.HasValue)
        {
            try
            {
                var batchSnap = await _batchRepo.GetByIdAsync(candidate.BatchId.Value, ct).ConfigureAwait(false);
                await SafePublishAsync(SignalREvents.IngestionProgress, new IngestionProgressEvent(
                    Path.GetFileName(candidate.Path),
                    batchSnap?.FilesProcessed ?? 0,
                    batchSnap?.FilesTotal ?? 0,
                    "Processing"), ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "IngestionProgress publish failed — pipeline continues");
            }
        }

        // Foreign-language metadata check removed — handled by LanguageMismatch trigger
        // in HydrationPipelineService (runs after Stage 1 with more context).

        // Step 11: staging-first flow.
        // ALL files go to .data/staging/ first — the Library only receives files that
        // have been hydrated and promoted by AutoOrganizeService.
        // Files land in a flat per-item folder ({assetId12}/); status is tracked in the
        // database. The "rejected/" subfolder is the only named subdirectory.
        bool hasUserLock = claims.Any(c => c.IsUserLocked);

        // Calculate the relative path once for the gate (needed for the "Other" check).
        string? gateRelativePath = _options.AutoOrganize
            && !string.IsNullOrWhiteSpace(_options.LibraryRoot)
            ? _organizer.CalculatePath(candidate, _options.ResolveTemplate(candidate.DetectedMediaType?.ToString()))
            : null;

        var candidateCanonicals = candidate.Metadata
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var gateResult = _gate.Evaluate(
            scored.OverallConfidence,
            candidateCanonicals,
            hasUserLock,
            mediaTypeNeedsReview,
            gateRelativePath);

        string currentPath = candidate.Path;
        if (_options.AutoOrganize
            && !string.IsNullOrWhiteSpace(_options.LibraryRoot))
        {
            string stagingSubcategory = gateResult.StagingSubcategory;

            _logger.LogInformation(
                "Moving to staging ({Subcategory}) — confidence {Confidence:P0} for {Path}",
                stagingSubcategory, scored.OverallConfidence, candidate.Path);

            currentPath = await MoveToStagingAsync(currentPath, stagingSubcategory, ct, assetId)
                              .ConfigureAwait(false) ?? currentPath;

            _logger.LogInformation(
                "Staging: asset {AssetId} staged at {Path}",
                assetId, currentPath);

            // Clean empty subdirectories left behind in the watch folder.
            CleanEmptyWatchParents(candidate.Path, _options.WatchDirectory);

            // History: staged.
            await SafeActivityLogAsync(new SystemActivityEntry
            {
                ActionType = SystemActionType.MovedToStaging,
                EntityId = assetId,
                Detail = $"Moved to staging: {stagingSubcategory}",
                IngestionRunId = ingestionRunId,
            }, ct).ConfigureAwait(false);

            if (gateResult.ReviewTrigger is not null)
            {
                await CreateIngestionReviewItemAsync(
                    assetId, gateResult.ReviewTrigger, scored.OverallConfidence,
                    gateResult.ReviewDetail!,
                    ct, ingestionRunId).ConfigureAwait(false);

                _logger.LogInformation(
                    "Review: asset {AssetId} queued for review — trigger={Trigger}",
                    assetId, gateResult.ReviewTrigger);

                // History: review created.
                await SafeActivityLogAsync(new SystemActivityEntry
                {
                    ActionType = SystemActionType.ReviewItemCreated,
                    EntityId = assetId,
                    Detail = $"Sent for review: {gateResult.ReviewTrigger}",
                    IngestionRunId = ingestionRunId,
                }, ct).ConfigureAwait(false);
            }
            else
            {
                _logger.LogWarning(
                    "Review gap: asset {AssetId} in staging but no review entry created",
                    assetId);
            }

            // Lifecycle log: staged.
            var logStatus = gateResult.ReviewTrigger is not null ? "needs_review" : "staged";
            try { await _ingestionLog.UpdateStatusAsync(logEntryId, logStatus,
                mediaType: resolvedMediaType.ToString(),
                ct: ct).ConfigureAwait(false); }
            catch (Exception ex) { _logger.LogDebug(ex, "Ingestion log update failed — continuing"); }
        }

        // Update stored path when the file was moved to staging.
        // This MUST happen before enqueuing hydration so that AutoOrganizeService
        // reads the staging path (not the watch folder path) when it promotes the
        // file to the organised library after hydration completes.
        if (!string.Equals(currentPath, candidate.Path, StringComparison.Ordinal))
            await _assetRepo.UpdateFilePathAsync(assetId, currentPath, ct).ConfigureAwait(false);

        // Phase 9→Pipeline: enqueue non-blocking three-stage hydration pipeline.
        // IMPORTANT: placed AFTER the organization gate so that any LowConfidence review
        // item created above already exists in the database before the hydration pipeline's
        // TryAutoResolveAndOrganizeAsync runs. This prevents a race condition where the
        // hydration pipeline resolves before the review item is even written, leaving a
        // stale review item in the queue for a file that was successfully organized.
        await _pipeline.EnqueueAsync(new HarvestRequest
        {
            EntityId            = assetId,
            EntityType          = EntityType.MediaAsset,
            MediaType           = resolvedMediaType,
            Hints               = BuildHints(candidate.Metadata),
            IngestionRunId      = ingestionRunId,
            FolderHintBridgeIds = null,
            HintedHubId         = null,
            Pass                = HydrationPass.Quick,
        }, ct).ConfigureAwait(false);

        await SafeActivityLogAsync(new Domain.Entities.SystemActivityEntry
        {
            ActionType     = Domain.Enums.SystemActionType.HydrationEnqueued,
            EntityId       = assetId,
            EntityType     = "MediaAsset",
            HubName        = resolvedTitle,
            ChangesJson    = JsonSerializer.Serialize(new { entity_id = assetId.ToString(), media_type = resolvedMediaType.ToString() }),
            Detail         = $"Queued for metadata enrichment ({resolvedMediaType})",
            IngestionRunId = ingestionRunId,
        }, ct).ConfigureAwait(false);

        // Phase 9: person enrichment is deferred to the hydration pipeline (Stage 1),
        // which has pen name detection logic.  Running it here causes a race condition:
        // the background MetadataHarvestingService would enrich "James S.A. Corey" with
        // a co-author's bio before the pipeline can detect it as a collective pseudonym.
        // HydrationPipelineService.ExtractPersonReferencesFromRawClaims handles person
        // creation, linking, and enrichment after pen name detection has run.

        // Step 11b: persist embedded cover art.
        // When ImagePathService is available, write to .images/works/_pending/{assetId12}/cover.jpg
        // so the image is decoupled from the staging/library path and survives file moves.
        // Without ImagePathService (legacy), write alongside the staging file as before.
        bool fileIsInStaging = !string.IsNullOrWhiteSpace(_options.StagingPath)
            && currentPath.StartsWith(_options.StagingPath, StringComparison.OrdinalIgnoreCase);
        if ((fileIsInStaging || _imagePathService is not null) && result.CoverImage is { Length: > 0 })
        {
            try
            {
                string coverPath;
                string coverFolder;
                if (_imagePathService is not null)
                {
                    // Write to .images/ — no QID yet at this stage (provisional slot).
                    coverPath   = _imagePathService.GetWorkCoverPath(wikidataQid: null, assetId);
                    coverFolder = Path.GetDirectoryName(coverPath) ?? string.Empty;
                    ImagePathService.EnsureDirectory(coverPath);
                }
                else
                {
                    coverFolder = Path.GetDirectoryName(currentPath) ?? string.Empty;
                    coverPath   = Path.Combine(coverFolder, "cover.jpg");
                }

                await File.WriteAllBytesAsync(coverPath, result.CoverImage, ct).ConfigureAwait(false);

                // Generate thumbnail (200px wide, quality 75) for list view performance.
                if (_imagePathService is not null)
                {
                    var thumbPath = _imagePathService.GetWorkCoverThumbPath(wikidataQid: null, assetId);
                    GenerateThumbnail(coverPath, thumbPath);
                }

                await SafeActivityLogAsync(new Domain.Entities.SystemActivityEntry
                {
                    ActionType     = Domain.Enums.SystemActionType.CoverArtSaved,
                    EntityId       = assetId,
                    EntityType     = "MediaAsset",
                    HubName        = resolvedTitle,
                    ChangesJson    = JsonSerializer.Serialize(new
                    {
                        cover_size_bytes = result.CoverImage.Length,
                        filename         = "cover.jpg",
                        folder           = coverFolder,
                        location         = _imagePathService is not null ? "images" : "staging",
                    }),
                    Detail         = $"Cover art saved ({result.CoverImage.Length / 1024} KB)",
                    IngestionRunId = ingestionRunId,
                }, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to write cover.jpg for {Path}", currentPath);
            }
        }

        // Step 12: write-back is deferred to promotion.
        // Writing back to a file in staging would change its content hash,
        // causing the watcher to re-detect it as a new file.
        // AutoOrganizeService handles write-back after promotion to the Library.
        bool fileIsInLibrary = !string.IsNullOrWhiteSpace(_options.LibraryRoot)
            && currentPath.StartsWith(_options.LibraryRoot, StringComparison.OrdinalIgnoreCase)
            && !fileIsInStaging;
        List<string> tagsWritten  = [];
        bool         coverWritten = false;
        if (_options.WriteBack && fileIsInLibrary && candidate.Metadata is not null)
        {
            var tagger = _taggers.FirstOrDefault(t => t.CanHandle(currentPath));
            if (tagger is not null)
            {
                await tagger.WriteTagsAsync(currentPath, candidate.Metadata, ct)
                             .ConfigureAwait(false);
                tagsWritten = [.. candidate.Metadata.Keys];

                if (result.CoverImage is { Length: > 0 })
                {
                    await tagger.WriteCoverArtAsync(currentPath, result.CoverImage, ct)
                                 .ConfigureAwait(false);
                    coverWritten = true;
                }

                await SafeActivityLogAsync(new Domain.Entities.SystemActivityEntry
                {
                    ActionType     = Domain.Enums.SystemActionType.MetadataTagsWritten,
                    EntityId       = assetId,
                    EntityType     = "MediaAsset",
                    HubName        = resolvedTitle,
                    ChangesJson    = JsonSerializer.Serialize(new
                    {
                        tags_written  = tagsWritten,
                        cover_written = coverWritten,
                        file          = Path.GetFileName(currentPath),
                    }),
                    Detail         = $"Tags written back to file ({tagsWritten.Count} field(s){(coverWritten ? " + cover art" : "")})",
                    IngestionRunId = ingestionRunId,
                }, ct).ConfigureAwait(false);

                // ── Timeline: sync writeback event ───────────────────────────
                try
                {
                    var writebackEvent = new EntityEvent
                    {
                        EntityId       = assetId,
                        EntityType     = "Work",
                        EventType      = "sync_writeback",
                        Stage          = null,
                        Trigger        = "ingestion",
                        IngestionRunId = ingestionRunId,
                        Detail         = $"Metadata written back to file: {tagsWritten.Count} field(s){(coverWritten ? " + cover art" : "")}",
                    };
                    await _timelineRepo.InsertEventAsync(writebackEvent, ct).ConfigureAwait(false);

                    // Record each written field as a field change.
                    if (candidate.Metadata.Count > 0)
                    {
                        var fieldChanges = candidate.Metadata
                            .Select(kvp => new EntityFieldChange
                            {
                                EventId    = writebackEvent.Id,
                                EntityId   = assetId,
                                Field      = kvp.Key,
                                NewValue   = kvp.Value,
                            })
                            .ToList();
                        await _timelineRepo.InsertFieldChangesAsync(fieldChanges, ct).ConfigureAwait(false);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Failed to write sync writeback timeline event for asset {Id}", assetId);
                }
            }
        }

        // ── Phase 2 activity: FileIngested — fires after the full pipeline ──
        // Summarises the outcome: organized, staged, or awaiting enrichment.
        // Rebuild richJson to include organization destination (PathUpdated is folded in).
        string? organizedTo = fileIsInLibrary ? currentPath
            : !string.Equals(currentPath, candidate.Path, StringComparison.Ordinal) ? "staging"
            : null;

        // Build hero URL if hero was generated for this asset.
        // Check .images/ first, then fall back to legacy location alongside the media file.
        bool heroExists = false;
        if (_imagePathService is not null)
        {
            heroExists = File.Exists(_imagePathService.GetWorkHeroPath(wikidataQid: null, assetId));
        }
        if (!heroExists && fileIsInLibrary)
        {
            heroExists = File.Exists(Path.Combine(Path.GetDirectoryName(currentPath) ?? "", "hero.jpg"));
        }
        string? heroUrl = heroExists ? $"/stream/{assetId}/hero" : null;

        // Build per-field provenance so the Dashboard can show exactly
        // how each metadata field was matched and which source won.
        var fieldProvenance = scored.FieldScores
            .Where(f => !string.IsNullOrEmpty(f.WinningValue))
            .Select(f => new
            {
                field       = f.Key,
                value       = f.WinningValue,
                confidence  = f.Confidence,
                source      = f.WinningProviderId == LocalProcessorProviderId ? "embedded"
                            : f.WinningProviderId.HasValue ? "provider" : "unknown",
                provider_id = f.WinningProviderId?.ToString(),
                conflicted  = f.IsConflicted,
            })
            .ToList();

        // Determine the primary match method for the summary.
        string matchMethod;
        var titleField = scored.FieldScores.FirstOrDefault(f => f.Key == MetadataFieldConstants.Title);
        if (titleField is not null && titleField.WinningProviderId == LocalProcessorProviderId)
            matchMethod = "embedded_metadata";
        else if (titleField is not null && titleField.WinningProviderId.HasValue)
            matchMethod = "provider_match";
        else
            matchMethod = "filename_fallback";

        var finalRichJson = JsonSerializer.Serialize(new
        {
            title         = resolvedTitle,
            author        = resolvedAuthor,
            year          = candidate.Metadata?.GetValueOrDefault(MetadataFieldConstants.Year, string.Empty) ?? string.Empty,
            media_type    = resolvedMediaType.ToString(),
            confidence    = scored.OverallConfidence,
            source_file   = Path.GetFileName(candidate.Path),
            source_path   = candidate.Path,
            description   = candidate.Metadata?.GetValueOrDefault(MetadataFieldConstants.Description, string.Empty) ?? string.Empty,
            entity_id     = assetId.ToString(),
            organized_to  = organizedTo,
            hero_url      = heroUrl,
            cover_url     = fileIsInLibrary ? $"/stream/{assetId}/cover" : (string?)null,
            match_method  = matchMethod,
            field_sources = fieldProvenance,
            tags_written  = tagsWritten,
            cover_written = coverWritten,
        });

        // "Sent to review" is determined by the hydration pipeline, not at this stage.
        // Use "awaiting enrichment" for all staged files; the MediaAdded activity entry
        // written at the end of HydrationPipelineService carries the real outcome.
        string matchLabel = matchMethod switch
        {
            "embedded_metadata" => "matched from embedded tags",
            "provider_match"    => "matched via provider",
            _                   => "matched from filename",
        };
        string outcome = fileIsInLibrary
            ? $"Ingested — \"{resolvedTitle}\" ({matchLabel}, {scored.OverallConfidence:P0}) → Library"
            : $"Ingested — \"{resolvedTitle}\" ({matchLabel}, {scored.OverallConfidence:P0}) — awaiting enrichment";

        await SafeActivityLogAsync(new Domain.Entities.SystemActivityEntry
        {
            ActionType     = Domain.Enums.SystemActionType.FileIngested,
            EntityId       = assetId,
            EntityType     = "MediaAsset",
            HubName        = resolvedTitle,
            ChangesJson    = finalRichJson,
            Detail         = outcome,
            IngestionRunId = ingestionRunId,
        }, ct).ConfigureAwait(false);

        // ── Performance log ──────────────────────────────────────────────────
        pipelineStopwatch.Stop();
        _logger.LogInformation(
            "[PERF] {FileName}: Total={TotalMs}ms Hash={HashMs}ms (type={MediaType}, confidence={Confidence:P0})",
            Path.GetFileName(candidate.Path),
            pipelineStopwatch.ElapsedMilliseconds,
            (long)hash.Elapsed.TotalMilliseconds,
            resolvedMediaType,
            scored.OverallConfidence);

        } // end hash lock try
        finally
        {
            hashLock.Release();
            _concurrencyGuard.ReleaseHashLock(hash.Hex);
        }

        // end of ProcessCandidateCoreAsync
    }

    private async Task HandleDeletedAsync(IngestionCandidate candidate, CancellationToken ct)
    {
        _logger.LogInformation("File deleted: {Path}", candidate.Path);

        // Look up the asset by its stored file path.
        // The file is gone so we can't hash it, but file_path_root is still in the DB.
        var asset = await _assetRepo.FindByPathRootAsync(candidate.Path, ct)
                                    .ConfigureAwait(false);

        if (asset is null)
        {
            _logger.LogDebug(
                "No asset record found for deleted path {Path} — nothing to orphan.",
                candidate.Path);
            return;
        }

        await _assetRepo.UpdateStatusAsync(asset.Id, Domain.Enums.AssetStatus.Orphaned, ct)
                        .ConfigureAwait(false);

        _logger.LogInformation(
            "Asset {AssetId} marked Orphaned (file no longer exists at {Path}).",
            asset.Id, candidate.Path);

        await SafePublishAsync(SignalREvents.MediaRemoved, new
        {
            asset_id  = asset.Id,
            file_path = candidate.Path,
            status    = "Orphaned",
        }, ct).ConfigureAwait(false);
    }

    // =========================================================================
    // Dry-run simulation
    // =========================================================================

    private async Task<IEnumerable<PendingOperation>> SimulateFileAsync(
        string filePath, CancellationToken ct)
    {
        var ops = new List<PendingOperation>();

        // Hash (read-only — no side effects).
        var hash = await _hasher.ComputeAsync(filePath, ct).ConfigureAwait(false);

        // Duplicate check.
        var existing = await _assetRepo.FindByHashAsync(hash.Hex, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            ops.Add(new PendingOperation
            {
                SourcePath      = filePath,
                DestinationPath = filePath,
                OperationKind   = "Skip",
                Reason          = $"Duplicate of existing asset (hash={hash.Hex[..12]})",
            });
            return ops;
        }

        // Process.
        var result = await _processors.ProcessAsync(filePath, ct).ConfigureAwait(false);
        if (result.IsCorrupt)
        {
            ops.Add(new PendingOperation
            {
                SourcePath      = filePath,
                DestinationPath = filePath,
                OperationKind   = "Quarantine",
                Reason          = result.CorruptReason,
            });
            return ops;
        }

        // Build a minimal candidate for path calculation.
        var assetId = Guid.NewGuid();
        var claims  = BuildClaims(assetId, result);
        var scored  = await _scorer.ScoreEntityAsync(new ScoringContext
        {
            EntityId        = assetId,
            Claims          = claims,
            ProviderWeights = new Dictionary<Guid, double> { [LocalProcessorProviderId] = 1.0 },
            Configuration   = new ScoringConfiguration(),
        }, ct).ConfigureAwait(false);

        var candidate = new IngestionCandidate
        {
            Path        = filePath,
            EventType   = FileEventType.Created,
            DetectedAt  = DateTimeOffset.UtcNow,
            ReadyAt     = DateTimeOffset.UtcNow,
        };
        candidate.Metadata          = BuildMetadataDict(scored);
        candidate.DetectedMediaType = result.DetectedType;

        // Simulate move.
        if (_options.AutoOrganize && !string.IsNullOrWhiteSpace(_options.LibraryRoot))
        {
            var dryRunTemplate = _options.ResolveTemplate(candidate.DetectedMediaType?.ToString());
            var relative = _organizer.CalculatePath(candidate, dryRunTemplate);
            // template already resolves the full relative path including filename
            var destPath = Path.Combine(_options.LibraryRoot, relative);

            ops.Add(new PendingOperation
            {
                SourcePath      = filePath,
                DestinationPath = destPath,
                OperationKind   = "Move",
                Reason          = $"AutoOrganize template: {dryRunTemplate}",
            });
        }

        // Simulate write-back.
        if (_options.WriteBack && candidate.Metadata is not null)
        {
            var tagger = _taggers.FirstOrDefault(t => t.CanHandle(filePath));
            if (tagger is not null)
            {
                ops.Add(new PendingOperation
                {
                    SourcePath      = filePath,
                    DestinationPath = filePath,
                    OperationKind   = "WriteTag",
                    Reason          = $"Tagger: {tagger.GetType().Name}; " +
                                      $"{candidate.Metadata.Count} tag(s)",
                });

                if (result.CoverImage is { Length: > 0 })
                    ops.Add(new PendingOperation
                    {
                        SourcePath      = filePath,
                        DestinationPath = filePath,
                        OperationKind   = "WriteCoverArt",
                        Reason          = $"Cover image {result.CoverImage.Length} bytes",
                    });
            }
        }

        return ops;
    }

    // =========================================================================
    // Initial directory scan
    // =========================================================================

    /// <summary>
    /// Enumerates every file already present in the Watch Folder and synthesises
    /// a <see cref="FileEvent.Created"/> for each one, feeding them into the
    /// <see cref="DebounceQueue"/>.  This ensures files that were dropped into the
    /// folder before the Engine started are processed through the normal pipeline.
    ///
    /// Duplicates are harmless: step 5 (hash-based duplicate check) in
    /// <see cref="ProcessCandidateAsync"/> short-circuits them instantly.
    /// </summary>
    private void ScanExistingFiles(string directory, bool includeSubdirectories)
    {
        var searchOption = includeSubdirectories
            ? SearchOption.AllDirectories
            : SearchOption.TopDirectoryOnly;

        // Fetch all known file paths from the database in a single query.
        // Files whose path is already tracked are skipped without being
        // enqueued, preventing a spurious batch from being created on restart
        // for files that were processed in a previous session.
        // Note: files that moved after processing (e.g. watch folder → staging)
        // will not be caught by this path check, but the hash-based duplicate
        // check inside ProcessCandidateAsync (step 5) serves as the safety net.
        // Normalize all stored paths to full, canonical forms so the comparison
        // below is robust against relative paths or casing differences (Windows).
        HashSet<string> knownPaths;
        try
        {
            var rawPaths = _assetRepo.GetAllFilePathsAsync().GetAwaiter().GetResult();
            knownPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in rawPaths)
            {
                try { knownPaths.Add(Path.GetFullPath(p)); }
                catch { /* malformed path — ignore */ }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load known file paths from database — skipping pre-filter");
            knownPaths = [];
        }

        // Create an ingestion batch for this scan.
        var batchId = Guid.NewGuid();

        int count = 0;
        int skipped = 0;
        try
        {
            foreach (var filePath in Directory.EnumerateFiles(directory, "*.*", searchOption))
            {
                // Skip files inside the .data directory — staging, images, and database
                // live here and must not be re-ingested automatically.
                if (filePath.Contains(Path.DirectorySeparatorChar + ".data" + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase)
                    || filePath.Contains('/' + ".data" + '/', StringComparison.OrdinalIgnoreCase))
                    continue;

                // Skip non-media files (sidecar data, cover art, manifests, etc.)
                // that may appear in the watch folder alongside media files.
                if (NonMediaExtensions.Contains(Path.GetExtension(filePath)))
                    continue;

                // Skip files already tracked by the database (path-based pre-filter).
                // The pipeline's hash check remains the authoritative duplicate guard
                // for files whose path has changed since initial ingestion.
                if (knownPaths.Contains(Path.GetFullPath(filePath)))
                {
                    skipped++;
                    continue;
                }

                _debounce.Enqueue(new FileEvent
                {
                    Path       = filePath,
                    EventType  = FileEventType.Created,
                    OccurredAt = DateTimeOffset.UtcNow,
                    BatchId    = batchId,
                });
                count++;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Initial scan of watch directory failed after {Count} files: {Dir}",
                count, directory);
        }

        if (skipped > 0)
            _logger.LogInformation(
                "Initial scan: skipped {Skipped} already-known file(s) from {Dir}",
                skipped, directory);

        if (count > 0)
        {
            _logger.LogInformation(
                "Initial scan: enqueued {Count} existing file(s) from {Dir}",
                count, directory);

            // Persist a batch record now that we know the exact file count.
            try
            {
                _batchRepo.CreateAsync(new IngestionBatch
                {
                    Id          = batchId,
                    Status      = "running",
                    SourcePath  = directory,
                    FilesTotal  = count,
                    StartedAt   = DateTimeOffset.UtcNow,
                }).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Batch record creation failed for scan batchId {BatchId} — pipeline continues", batchId);
            }
        }

    }

    // =========================================================================
    // Polling fallback — safety net when FSW misses events
    // =========================================================================

    /// <summary>
    /// Periodically sweeps the Watch Folder for files that the
    /// <see cref="System.IO.FileSystemWatcher"/> may have missed.
    /// Synthesises <c>Created</c> events into the debounce queue;
    /// the normal hash-based duplicate check prevents double-processing.
    /// </summary>
    private async Task PollWatchDirectoryAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(_options.PollIntervalSeconds);
        _logger.LogInformation(
            "Polling fallback active: sweeping Watch Folder every {Seconds}s",
            _options.PollIntervalSeconds);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }

            if (string.IsNullOrWhiteSpace(_options.WatchDirectory)
                || !Directory.Exists(_options.WatchDirectory))
                continue;

            try
            {
                var searchOption = _options.IncludeSubdirectories
                    ? SearchOption.AllDirectories
                    : SearchOption.TopDirectoryOnly;

                // Re-enqueue every file in the watcher on each sweep.
                // Files that have already been organized are moved out of the
                // watcher directory and will naturally disappear from subsequent
                // sweeps. Files still present get routed through the hash-based
                // duplicate check: if already ingested, TryReorganizeExistingAsync
                // moves them to the library. The debounce queue coalesces rapid
                // events for the same path, preventing queue flooding.
                int enqueued = 0;
                foreach (var filePath in Directory.EnumerateFiles(
                             _options.WatchDirectory, "*.*", searchOption))
                {
                    if (ct.IsCancellationRequested) break;

                    // Skip files inside the .data directory (staging, images, database).
                    if (filePath.Contains(Path.DirectorySeparatorChar + ".data" + Path.DirectorySeparatorChar,
                            StringComparison.OrdinalIgnoreCase)
                        || filePath.Contains('/' + ".data" + '/', StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Skip non-media files (sidecar data, cover art, manifests).
                    if (NonMediaExtensions.Contains(Path.GetExtension(filePath)))
                        continue;

                    var pollEvt = new FileEvent
                    {
                        Path      = filePath,
                        EventType = FileEventType.Created,
                        OccurredAt = DateTimeOffset.UtcNow,
                    };
                    BufferFswEvent(pollEvt);
                    enqueued++;
                }

                if (enqueued > 0)
                    _logger.LogInformation(
                        "Poll sweep: enqueued {Count} file(s) from {Dir}",
                        enqueued, _options.WatchDirectory);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Poll sweep failed for {Dir}", _options.WatchDirectory);
            }
        }
    }

    // =========================================================================
    // Re-organize already-ingested files still sitting in the Watch Folder
    // =========================================================================

    /// <summary>
    /// When a file's content hash is already in the database but the file is
    /// still sitting in the Watch Folder (not auto-organized on first ingest),
    /// attempt to organize it using the canonical values that may have been
    /// enriched by external providers since the initial scan.
    /// </summary>
    private async Task TryReorganizeExistingAsync(
        MediaAsset existing, string currentPath, CancellationToken ct)
    {
        // Only attempt if the file is currently in the Watch Folder.
        if (string.IsNullOrWhiteSpace(_options.WatchDirectory)
            || !currentPath.StartsWith(
                    _options.WatchDirectory, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug(
                "Duplicate (hash={Hash}) not in Watch Folder; skipping: {Path}",
                existing.ContentHash[..12], currentPath);
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.LibraryRoot))
        {
            _logger.LogInformation(
                "Cannot re-organize {Hash}: LibraryRoot is not configured. " +
                "Set a Library Folder in Server Settings.",
                existing.ContentHash[..12]);
            return;
        }

        // Load existing canonical values — these contain the resolved metadata
        // (possibly enriched by external providers since the initial scan).
        var canonicals = await _canonicalRepo.GetByEntityAsync(existing.Id, ct)
                                             .ConfigureAwait(false);
        if (canonicals.Count == 0)
        {
            _logger.LogInformation(
                "Cannot re-organize {Hash}: no canonical values found for asset {Id}.",
                existing.ContentHash[..12], existing.Id);
            return;
        }

        var metadata = canonicals.ToDictionary(
            c => c.Key, c => c.Value, StringComparer.OrdinalIgnoreCase);

        // Determine media type from canonical values or fall back to Unknown.
        var mediaType = metadata.TryGetValue(MetadataFieldConstants.MediaTypeField, out var mtStr)
            && Enum.TryParse<MediaType>(mtStr, ignoreCase: true, out var mt)
                ? mt
                : (MediaType?)null;

        // Confidence gate: only re-organize when metadata is trustworthy.
        var reorgClaims = await _claimRepo.GetByEntityAsync(existing.Id, ct).ConfigureAwait(false);
        var reorgScoringContext = new ScoringContext
        {
            EntityId        = existing.Id,
            Claims          = reorgClaims,
            ProviderWeights = new Dictionary<Guid, double>
                { [LocalProcessorProviderId] = 1.0 },
            Configuration   = new ScoringConfiguration(),
        };
        var reorgScored = await _scorer.ScoreEntityAsync(reorgScoringContext, ct).ConfigureAwait(false);
        bool reorgHasUserLock    = reorgClaims.Any(c => c.IsUserLocked);
        bool reorgHighConfidence = reorgScored.OverallConfidence >= _scoringConfig.AutoLinkThreshold;
        if (!reorgHighConfidence && !reorgHasUserLock)
        {
            _logger.LogInformation(
                "Re-organize skipped for {Hash}: confidence {Confidence:P0} below threshold ({Threshold:P0})",
                existing.ContentHash[..12], reorgScored.OverallConfidence, _scoringConfig.AutoLinkThreshold);

            // Move to staging so the file doesn't loop on every poll sweep.
            // Only fires when the file is still in the Watch Folder (MoveToStagingAsync
            // is a no-op for files already in staging or the Library).
            string lowSubcategory = reorgScored.OverallConfidence < 0.40
                ? "unidentifiable"
                : "low-confidence";
            var lowStaged = await MoveToStagingAsync(currentPath, lowSubcategory, ct, existing.Id)
                                     .ConfigureAwait(false);
            if (lowStaged is not null)
            {
                CleanEmptyWatchParents(currentPath, _options.WatchDirectory);
                await _assetRepo.UpdateFilePathAsync(existing.Id, lowStaged, ct)
                                 .ConfigureAwait(false);
                await CreateIngestionReviewItemAsync(
                    existing.Id, ReviewTrigger.LowConfidence,
                    reorgScored.OverallConfidence,
                    $"Confidence {reorgScored.OverallConfidence:P0} below organization " +
                    "threshold. Staged for review.",
                    ct).ConfigureAwait(false);
            }
            return;
        }

        // Build a synthetic candidate so the FileOrganizer can calculate the path.
        var synth = new IngestionCandidate
        {
            Path       = currentPath,
            EventType  = FileEventType.Created,
            DetectedAt = DateTimeOffset.UtcNow,
            ReadyAt    = DateTimeOffset.UtcNow,
            Metadata          = metadata,
            DetectedMediaType = mediaType,
        };

        var reorgTemplate = _options.ResolveTemplate(mediaType?.ToString());
        var relative = _organizer.CalculatePath(synth, reorgTemplate);

        // Guard: never re-organize into the "Other" category. If the media type
        // couldn't be determined from canonical values, move to staging and create
        // a review item so the user can manually classify the file.
        if (relative.StartsWith("Other", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Re-organization blocked for {Hash} — resolved category is 'Other'. " +
                "Moving to staging and creating review item.",
                existing.ContentHash[..12]);

            var otherStaged = await MoveToStagingAsync(currentPath, "low-confidence", ct, existing.Id).ConfigureAwait(false);
            if (otherStaged is not null)
            {
                CleanEmptyWatchParents(currentPath, _options.WatchDirectory);
                await _assetRepo.UpdateFilePathAsync(existing.Id, otherStaged, ct).ConfigureAwait(false);
            }

            await CreateIngestionReviewItemAsync(
                existing.Id, ReviewTrigger.LowConfidence, 0.0,
                $"Re-organization would place file in 'Other' category (media type unknown). " +
                "File moved to staging for manual classification.",
                ct).ConfigureAwait(false);
            return;
        }

        // Placeholder title guard: stage as low-confidence when the title is a
        // well-known placeholder and no bridge ID confirms identity.
        string? reorgTitle = metadata.GetValueOrDefault(MetadataFieldConstants.Title);
        bool isPlaceholder = MetadataGuards.IsPlaceholderTitle(reorgTitle)
            && !MetadataGuards.HasBridgeId(metadata);

        // Staging-first: move to staging rather than directly to library.
        // AutoOrganizeService will promote to library after hydration.
        string subcategory = isPlaceholder ? "low-confidence"
            : relative.StartsWith("Other", StringComparison.OrdinalIgnoreCase) ? "other"
            : "pending";

        var staged = await MoveToStagingAsync(currentPath, subcategory, ct, existing.Id)
                          .ConfigureAwait(false);
        if (staged is not null)
        {
            CleanEmptyWatchParents(currentPath, _options.WatchDirectory);
            await _assetRepo.UpdateFilePathAsync(existing.Id, staged, ct)
                            .ConfigureAwait(false);

            _logger.LogInformation(
                "Staged existing asset {Hash} → {Dest} ({Subcategory})",
                existing.ContentHash[..12], staged, subcategory);

            // Re-enqueue hydration so AutoOrganizeService can promote after enrichment.
            await _pipeline.EnqueueAsync(new HarvestRequest
            {
                EntityId              = existing.Id,
                EntityType            = EntityType.MediaAsset,
                MediaType             = mediaType ?? MediaType.Unknown,
                Hints                 = BuildHints(metadata),
                SuppressActivityEntry = true,
                Pass                  = HydrationPass.Quick,
            }, ct).ConfigureAwait(false);

            if (isPlaceholder)
            {
                await CreateIngestionReviewItemAsync(
                    existing.Id, ReviewTrigger.PlaceholderTitle, reorgScored.OverallConfidence,
                    $"Title \"{reorgTitle ?? "(blank)"}\" is a placeholder with no bridge IDs. Staged for review.",
                    ct).ConfigureAwait(false);
            }
        }
    }

    // =========================================================================
    // .orphans → .data/staging migration
    // =========================================================================

    /// <summary>
    /// One-time migration: renames the legacy <c>.orphans/</c> directory to
    /// <c>.data/staging/</c> and updates all <c>MediaAsset.FilePathRoot</c> records
    /// that reference the old path.
    /// </summary>
    private async Task MigrateOrphansToStagingAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.LibraryRoot))
            return;

        var legacyPath = Path.Combine(_options.LibraryRoot, ".orphans");
        var stagingPath = _options.StagingPath;

        if (!Directory.Exists(legacyPath) || Directory.Exists(stagingPath))
            return;

        try
        {
            Directory.Move(legacyPath, stagingPath);
            _logger.LogInformation(
                "Migrated .orphans → .data/staging: {Legacy} → {Staging}",
                legacyPath, stagingPath);

            // Update DB records that reference the old .orphans path.
            // Use ListByStatusAsync for each status to find assets with stale paths.
            int updated = 0;
            foreach (var status in Enum.GetValues<AssetStatus>())
            {
                var assets = await _assetRepo.ListByStatusAsync(status, ct).ConfigureAwait(false);
                foreach (var asset in assets)
                {
                    if (asset.FilePathRoot.Contains(".orphans", StringComparison.OrdinalIgnoreCase))
                    {
                        // Replace .orphans prefix with the current StagingPath.
                        var oldPrefix = Path.Combine(_options.LibraryRoot, ".orphans");
                        var newPath = asset.FilePathRoot.Replace(
                            oldPrefix, stagingPath, StringComparison.OrdinalIgnoreCase);
                        await _assetRepo.UpdateFilePathAsync(asset.Id, newPath, ct)
                            .ConfigureAwait(false);
                        updated++;
                    }
                }
            }

            if (updated > 0)
                _logger.LogInformation(
                    "Updated {Count} asset path(s) from .orphans to .data/staging", updated);

            await SafeActivityLogAsync(new Domain.Entities.SystemActivityEntry
            {
                ActionType = Domain.Enums.SystemActionType.PathUpdated,
                EntityType = "Migration",
                Detail     = $"Migrated .orphans → .data/staging ({updated} asset path(s) updated)",
            }, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to migrate .orphans → .data/staging");
        }
    }

    // =========================================================================
    // Staging
    // =========================================================================

    /// <summary>
    /// Moves a file from the Watch Folder to the staging directory ({LibraryRoot}/.data/staging/).
    /// All files land in a flat per-item subdirectory using the first 12 chars of the asset GUID.
    /// Status is tracked in the database — no subcategory subfolders on disk (except "rejected/").
    /// Returns the new path on success, or <see langword="null"/> when LibraryRoot
    /// is not configured or the move fails.
    /// </summary>
    private async Task<string?> MoveToStagingAsync(
        string currentPath, string subcategory, CancellationToken ct,
        Guid? assetId = null)
    {
        if (string.IsNullOrWhiteSpace(_options.StagingPath))
            return null;

        // Only stage files that are currently in the Watch Folder.
        if (!string.IsNullOrWhiteSpace(_options.WatchDirectory)
            && !currentPath.StartsWith(
                    _options.WatchDirectory, StringComparison.OrdinalIgnoreCase))
            return null;

        // "rejected" is the only named subcategory on disk — all other statuses are
        // tracked in the database. All non-rejected files go into a flat per-item
        // subdirectory using the first 12 chars of the asset GUID.
        string stagingSubDir;
        bool isRejected = string.Equals(subcategory, "rejected", StringComparison.OrdinalIgnoreCase);
        if (isRejected)
        {
            stagingSubDir = Path.Combine(_options.StagingPath, "rejected");
        }
        else if (assetId.HasValue)
        {
            var itemDir = assetId.Value.ToString("N")[..12];
            stagingSubDir = Path.Combine(_options.StagingPath, itemDir);
        }
        else
        {
            stagingSubDir = _options.StagingPath;
        }
        Directory.CreateDirectory(stagingSubDir);

        var destPath = Path.Combine(stagingSubDir, Path.GetFileName(currentPath));

        bool moved = await _organizer.ExecuteMoveAsync(currentPath, destPath, ct)
                                      .ConfigureAwait(false);
        if (moved)
        {
            _logger.LogInformation(
                "Staged file: {Source} → {Dest} ({Subcategory})",
                currentPath, destPath, subcategory);

            await SafeActivityLogAsync(new Domain.Entities.SystemActivityEntry
            {
                ActionType = Domain.Enums.SystemActionType.MovedToStaging,
                EntityType = "MediaAsset",
                Detail     = $"Staged {Path.GetFileName(currentPath)} ({subcategory})",
            }, ct).ConfigureAwait(false);

            return destPath;
        }

        _logger.LogWarning(
            "Staging move failed for {Source} → {Dest}", currentPath, destPath);
        return null;
    }

    // =========================================================================
    // Staged asset cleanup
    // =========================================================================

    /// <summary>
    /// Cleans up a staged asset whose file no longer exists on disk.
    /// Removes filesystem artifacts (cover.jpg, empty folders)
    /// and deletes all associated DB records (claims, canonical values, asset).
    /// </summary>
    private async Task CleanStagedAssetAsync(
        MediaAsset staged, CancellationToken ct)
    {
        _logger.LogInformation(
            "Cleaning staged asset {Id} — file missing at {Path}",
            staged.Id, staged.FilePathRoot);

        // 1. Clean filesystem artifacts from the edition folder.
        var editionFolder = Path.GetDirectoryName(staged.FilePathRoot);
        if (!string.IsNullOrEmpty(editionFolder) && Directory.Exists(editionFolder))
        {
            SafeDeleteFile(Path.Combine(editionFolder, "cover.jpg"));
            SafeDeleteFile(Path.Combine(editionFolder, "hero.jpg"));
            TryDeleteEmptyDirectory(editionFolder);
        }

        // 1b. Clean .images/ pending directory for this asset (if ImagePathService is active).
        if (_imagePathService is not null)
        {
            try
            {
                var pendingDir = Path.Combine(
                    _imagePathService.ImagesRoot, "works", "_pending",
                    staged.Id.ToString("N")[..12]);
                if (Directory.Exists(pendingDir))
                    Directory.Delete(pendingDir, recursive: true);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to clean .images/_pending for asset {Id}", staged.Id);
            }
        }

        // 2. Delete DB records: claims → canonical values → asset.
        await _claimRepo.DeleteByEntityAsync(staged.Id, ct).ConfigureAwait(false);
        await _canonicalRepo.DeleteByEntityAsync(staged.Id, ct).ConfigureAwait(false);
        await _assetRepo.DeleteAsync(staged.Id, ct).ConfigureAwait(false);

        // 3. Log activity.
        await SafeActivityLogAsync(new Domain.Entities.SystemActivityEntry
        {
            ActionType = Domain.Enums.SystemActionType.StagedFileCleaned,
            EntityId   = staged.Id,
            EntityType = "MediaAsset",
            Detail     = $"Staged asset cleaned: {Path.GetFileName(staged.FilePathRoot)} (file missing)",
        }, ct).ConfigureAwait(false);
    }

    private static void SafeDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            // Best-effort: if the file is locked, skip silently.
        }
    }

    private static void TryDeleteEmptyDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path) &&
                !Directory.EnumerateFileSystemEntries(path).Any())
            {
                Directory.Delete(path);
            }
        }
        catch (IOException)
        {
            // Best-effort: if the directory is locked, skip silently.
        }
    }

    /// <summary>
    /// Recursively deletes empty parent directories of a source file up to (but
    /// not including) the watch folder root. Prevents empty subdirectories from
    /// accumulating in the watch folder after files are moved to the library.
    /// </summary>
    private static void CleanEmptyWatchParents(string sourceFilePath, string? watchRoot)
    {
        if (string.IsNullOrEmpty(watchRoot)) return;

        try
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(sourceFilePath)!);
            var stopNorm = watchRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            while (dir is not null && dir.Exists)
            {
                var dirNorm = dir.FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                // Stop when we reach the watch root — never delete it.
                if (string.Equals(dirNorm, stopNorm, StringComparison.OrdinalIgnoreCase))
                    break;

                if (dir.EnumerateFileSystemInfos().Any())
                    break; // Not empty — stop climbing.

                var parent = dir.Parent;
                dir.Delete();
                dir = parent;
            }
        }
        catch
        {
            // Best-effort cleanup — if the directory is locked or inaccessible, skip.
        }
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static IReadOnlyList<MetadataClaim> BuildClaims(
        Guid entityId,
        Processors.Models.ProcessorResult result)
    {
        return result.Claims
            .Select(c => new MetadataClaim
            {
                Id          = Guid.NewGuid(),
                EntityId    = entityId,
                ProviderId  = LocalProcessorProviderId,
                ClaimKey    = c.Key,
                ClaimValue  = c.Value,
                Confidence  = c.Confidence,
                ClaimedAt   = DateTimeOffset.UtcNow,
            })
            .ToList();
    }

    private static IReadOnlyDictionary<string, string> BuildMetadataDict(
        Intelligence.Models.ScoringResult scored)
    {
        return scored.FieldScores
            .Where(f => !string.IsNullOrEmpty(f.WinningValue))
            .ToDictionary(
                f => f.Key,
                f => f.WinningValue!,
                StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Builds a hint dictionary from the resolved canonical metadata for use
    /// in a <see cref="HarvestRequest"/>.
    /// </summary>
    private static IReadOnlyDictionary<string, string> BuildHints(
        IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null or { Count: 0 })
            return new Dictionary<string, string>();

        var hints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Core metadata keys for title search and person enrichment.
        // Bridge identifier keys that enable direct Wikidata lookup (Tier 1)
        // and satisfy the HasSufficientMetadataForAuthorityMatch gate in the
        // hydration pipeline.  Without these, audiobooks and other media
        // lacking ISBN/ASIN fall through to the review queue unnecessarily.
        foreach (var key in new[]
        {
            // Core
            MetadataFieldConstants.Title, MetadataFieldConstants.Author, MetadataFieldConstants.Narrator,
            MetadataFieldConstants.Year, MetadataFieldConstants.Series, MetadataFieldConstants.SeriesPosition,
            // Media-specific fields for attribute-targeted provider search
            MetadataFieldConstants.ShowName, MetadataFieldConstants.EpisodeTitle,
            MetadataFieldConstants.Album, MetadataFieldConstants.Artist,
            MetadataFieldConstants.SeasonNumber, MetadataFieldConstants.EpisodeNumber,
            MetadataFieldConstants.TrackNumber, MetadataFieldConstants.DurationField,
            MetadataFieldConstants.Composer, MetadataFieldConstants.Director,
            MetadataFieldConstants.Genre, MetadataFieldConstants.PodcastName,
            // Bridge identifiers
            BridgeIdKeys.Asin, BridgeIdKeys.Isbn, BridgeIdKeys.TmdbId, BridgeIdKeys.ImdbId, BridgeIdKeys.GoodreadsId,
            BridgeIdKeys.MusicBrainzId, BridgeIdKeys.AppleBooksId, "audible_asin", BridgeIdKeys.OpenLibraryId,
            BridgeIdKeys.ComicVineId, "apple_podcasts_id",
        })
        {
            if (metadata.TryGetValue(key, out var value) &&
                !string.IsNullOrWhiteSpace(value))
                hints[key] = value;
        }
        return hints;
    }

    /// <summary>
    /// Extracts author and narrator person references from resolved metadata.
    /// Returns an empty list if neither field is present.
    /// </summary>
    private static IReadOnlyList<PersonReference> ExtractPersonReferences(
        IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null or { Count: 0 })
            return [];

        var refs = new List<PersonReference>();

        if (metadata.TryGetValue(MetadataFieldConstants.Author, out var author) &&
            !string.IsNullOrWhiteSpace(author))
            refs.Add(new PersonReference("Author", author.Trim()));

        if (metadata.TryGetValue(MetadataFieldConstants.Narrator, out var narrator) &&
            !string.IsNullOrWhiteSpace(narrator))
            refs.Add(new PersonReference("Narrator", narrator.Trim()));

        return refs;
    }

    /// <summary>
    /// Creates a review queue entry from the ingestion pipeline. Used when the
    /// confidence gate blocks organization or when the file would be placed in
    /// the "Other" category.
    /// </summary>
    private async Task CreateIngestionReviewItemAsync(
        Guid entityId,
        string trigger,
        double confidence,
        string detail,
        CancellationToken ct,
        Guid? ingestionRunId = null)
    {
        try
        {
            // Check if a pending review item already exists for this entity
            // (the hydration pipeline may also create one asynchronously).
            var existing = await _reviewRepo.GetByEntityAsync(entityId, ct)
                .ConfigureAwait(false);

            if (existing.Any(r => r.Status == Domain.Enums.ReviewStatus.Pending
                                  && r.Trigger == trigger))
            {
                _logger.LogDebug(
                    "Review item '{Trigger}' already exists for entity {Id} — skipping duplicate.",
                    trigger, entityId);
                return;
            }

            var entry = new ReviewQueueEntry
            {
                Id              = Guid.NewGuid(),
                EntityId        = entityId,
                EntityType      = "MediaAsset",
                Trigger         = trigger,
                ConfidenceScore = confidence,
                Detail          = detail,
                CreatedAt       = DateTimeOffset.UtcNow,
            };

            await _reviewRepo.InsertAsync(entry, ct).ConfigureAwait(false);

            // Activity: review item created.
            await SafeActivityLogAsync(new Domain.Entities.SystemActivityEntry
            {
                ActionType = Domain.Enums.SystemActionType.ReviewItemCreated,
                EntityId   = entityId,
                EntityType = "MediaAsset",
                Detail     = $"Sent to review: {trigger} — {detail}",
                IngestionRunId = ingestionRunId,
            }, ct).ConfigureAwait(false);

            await SafePublishAsync(SignalREvents.ReviewItemCreated, new
            {
                review_id   = entry.Id,
                entity_id   = entityId,
                trigger,
                confidence,
            }, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Review item created for entity {Id}: {Trigger} — {Detail}",
                entityId, trigger, detail);
        }
        catch (Exception ex)
        {
            // Review item creation failure must not abort the ingestion pipeline.
            _logger.LogWarning(ex,
                "Failed to create review item for entity {Id}", entityId);
        }
    }

    /// <summary>
    /// Creates an <see cref="ReviewTrigger.AmbiguousMediaType"/> review queue entry
    /// with the full list of media type candidates serialized as JSON.
    /// </summary>
    private async Task CreateAmbiguousMediaTypeReviewItemAsync(
        Guid entityId,
        double confidence,
        string candidatesJson,
        string detail,
        CancellationToken ct,
        Guid? ingestionRunId = null)
    {
        try
        {
            var existing = await _reviewRepo.GetByEntityAsync(entityId, ct)
                .ConfigureAwait(false);

            if (existing.Any(r => r.Status == Domain.Enums.ReviewStatus.Pending
                                  && r.Trigger == ReviewTrigger.AmbiguousMediaType))
            {
                _logger.LogDebug(
                    "AmbiguousMediaType review item already exists for entity {Id} — skipping.",
                    entityId);
                return;
            }

            var entry = new ReviewQueueEntry
            {
                Id              = Guid.NewGuid(),
                EntityId        = entityId,
                EntityType      = "MediaAsset",
                Trigger         = ReviewTrigger.AmbiguousMediaType,
                ConfidenceScore = confidence,
                CandidatesJson  = candidatesJson,
                Detail          = detail,
                CreatedAt       = DateTimeOffset.UtcNow,
            };

            await _reviewRepo.InsertAsync(entry, ct).ConfigureAwait(false);

            await SafeActivityLogAsync(new Domain.Entities.SystemActivityEntry
            {
                ActionType = Domain.Enums.SystemActionType.ReviewItemCreated,
                EntityId   = entityId,
                EntityType = "MediaAsset",
                Detail     = $"Ambiguous media type: {detail}",
                IngestionRunId = ingestionRunId,
            }, ct).ConfigureAwait(false);

            await SafePublishAsync(SignalREvents.ReviewItemCreated, new
            {
                review_id   = entry.Id,
                entity_id   = entityId,
                trigger     = ReviewTrigger.AmbiguousMediaType,
                confidence,
            }, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "AmbiguousMediaType review item created for entity {Id}: {Detail}",
                entityId, detail);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to create AmbiguousMediaType review item for entity {Id}", entityId);
        }
    }

    /// <summary>
    /// Creates a <see cref="ReviewTrigger.MetadataConflict"/> review queue entry
    /// when the scoring engine detects conflicting canonical values. The file still
    /// organises with the best guess — conflicts don't block the confidence gate.
    /// </summary>
    private async Task CreateMetadataConflictReviewItemAsync(
        Guid entityId,
        double confidence,
        List<string> conflictedFields,
        CancellationToken ct,
        Guid? ingestionRunId = null)
    {
        try
        {
            var existing = await _reviewRepo.GetByEntityAsync(entityId, ct)
                .ConfigureAwait(false);

            if (existing.Any(r => r.Status == Domain.Enums.ReviewStatus.Pending
                                  && r.Trigger == ReviewTrigger.MetadataConflict))
            {
                _logger.LogDebug(
                    "MetadataConflict review item already exists for entity {Id} — skipping.",
                    entityId);
                return;
            }

            var detail = $"Conflicting metadata: {string.Join(", ", conflictedFields)}";
            var entry = new ReviewQueueEntry
            {
                Id              = Guid.NewGuid(),
                EntityId        = entityId,
                EntityType      = "MediaAsset",
                Trigger         = ReviewTrigger.MetadataConflict,
                ConfidenceScore = confidence,
                Detail          = detail,
                CreatedAt       = DateTimeOffset.UtcNow,
            };

            await _reviewRepo.InsertAsync(entry, ct).ConfigureAwait(false);

            await SafeActivityLogAsync(new Domain.Entities.SystemActivityEntry
            {
                ActionType = Domain.Enums.SystemActionType.ReviewItemCreated,
                EntityId   = entityId,
                EntityType = "MediaAsset",
                Detail     = detail,
                IngestionRunId = ingestionRunId,
            }, ct).ConfigureAwait(false);

            await SafePublishAsync(SignalREvents.ReviewItemCreated, new
            {
                review_id   = entry.Id,
                entity_id   = entityId,
                trigger     = ReviewTrigger.MetadataConflict,
                confidence,
            }, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "MetadataConflict review item created for entity {Id}: {Detail}",
                entityId, detail);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to create MetadataConflict review item for entity {Id}", entityId);
        }
    }

    /// <summary>
    /// Publishes an event without propagating exceptions to the calling pipeline.
    /// A publish failure (e.g. transient SignalR error) must never abort file ingestion.
    /// </summary>
    private async Task SafePublishAsync<TPayload>(
        string eventName, TPayload payload, CancellationToken ct)
        where TPayload : notnull
    {
        try
        {
            await _publisher.PublishAsync(eventName, payload, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Event publish failed for '{Event}' — pipeline continues", eventName);
        }
    }

    /// <summary>
    /// Adds an FSW/poll-sourced file event to the collection buffer.
    /// Resets the quiet-period timer. Events that already carry a BatchId
    /// (e.g. from <see cref="ScanExistingFiles"/>) are passed directly to
    /// the debounce queue.
    /// </summary>
    private void BufferFswEvent(FileEvent evt)
    {
        // Events from ScanExistingFiles already have a batch — pass through.
        if (evt.BatchId is not null)
        {
            _debounce.Enqueue(evt);
            return;
        }

        lock (_fswBufferLock)
        {
            // Deduplicate: skip if this path was already enqueued in ANY batch
            // (prevents poll sweep re-detecting files still being processed).
            if (!_enqueuedPaths.Add(evt.Path))
                return;

            _fswBufferedPaths.Add(evt.Path);
            _fswBuffer.Add(evt);

            // Reset (or start) the quiet-period timer.
            _fswFlushTimer?.Dispose();
            _fswFlushTimer = new Timer(
                _ => FlushFswBuffer(),
                null,
                FswQuietPeriod,
                Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>
    /// Called when the quiet period expires. Creates a batch record with the
    /// exact file count, stamps every buffered event with the batch ID,
    /// and flushes them all into the debounce queue for processing.
    /// </summary>
    private async void FlushFswBuffer()
    {
        List<FileEvent> snapshot;
        lock (_fswBufferLock)
        {
            if (_fswBuffer.Count == 0) return;
            snapshot = [.. _fswBuffer];
            _fswBuffer.Clear();
            _fswBufferedPaths.Clear();
            _fswFlushTimer?.Dispose();
            _fswFlushTimer = null;
        }

        var batchId = Guid.NewGuid();

        // Persist a batch record with the exact file count before any file is processed.
        try
        {
            await _batchRepo.CreateAsync(new IngestionBatch
            {
                Id          = batchId,
                Status      = "running",
                SourcePath  = _options.WatchDirectory,
                FilesTotal  = snapshot.Count,
                StartedAt   = DateTimeOffset.UtcNow,
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Batch record creation failed for FSW flush batchId {BatchId} — pipeline continues", batchId);
        }

        // Stamp and enqueue all events.
        foreach (var evt in snapshot)
        {
            evt.BatchId = batchId;
            _debounce.Enqueue(evt);
        }
    }

    /// <summary>
    /// Writes a <see cref="Domain.Entities.SystemActivityEntry"/> to the activity ledger
    /// without propagating exceptions.  Activity logging must never abort the pipeline.
    /// </summary>
    private async Task SafeActivityLogAsync(
        Domain.Entities.SystemActivityEntry entry,
        CancellationToken ct)
    {
        try
        {
            await _activityRepo.LogAsync(entry, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Activity log write failed for action '{Action}' — pipeline continues",
                entry.ActionType);
        }
    }

    /// <summary>
    /// Generates a 200px-wide JPEG thumbnail from an existing cover.jpg.
    /// Best-effort — never throws; ingestion continues if thumbnail generation fails.
    /// </summary>
    private static void GenerateThumbnail(string coverPath, string thumbPath)
    {
        try
        {
            using var inputStream = File.OpenRead(coverPath);
            using var bitmap = SkiaSharp.SKBitmap.Decode(inputStream);
            if (bitmap is null) return;

            var targetWidth  = 200;
            var targetHeight = (int)(bitmap.Height * (200.0 / bitmap.Width));
            using var resized = bitmap.Resize(
                new SkiaSharp.SKImageInfo(targetWidth, targetHeight),
                new SkiaSharp.SKSamplingOptions(SkiaSharp.SKFilterMode.Linear));
            if (resized is null) return;

            using var image = SkiaSharp.SKImage.FromBitmap(resized);
            using var data  = image.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 75);

            Domain.Services.ImagePathService.EnsureDirectory(thumbPath);
            using var output = File.OpenWrite(thumbPath);
            data.SaveTo(output);
        }
        catch
        {
            // Thumbnail generation is best-effort — don't fail ingestion.
        }
    }
}
