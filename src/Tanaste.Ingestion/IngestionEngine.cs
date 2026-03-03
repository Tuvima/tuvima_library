using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tanaste.Domain.Aggregates;
using Tanaste.Domain.Contracts;
using Tanaste.Domain.Entities;
using Tanaste.Domain.Enums;
using Tanaste.Domain.Events;
using Tanaste.Domain.Models;
using Tanaste.Ingestion.Contracts;
using Tanaste.Ingestion.Models;
using Tanaste.Intelligence.Contracts;
using Tanaste.Intelligence.Models;
using Tanaste.Processors.Contracts;

namespace Tanaste.Ingestion;

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
    private static readonly Guid LocalProcessorProviderId =
        new("a1b2c3d4-e5f6-4700-8900-0a1b2c3d4e5f");

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

    // Phase 7: sidecar XML writer.
    private readonly ISidecarWriter _sidecar;

    // Hub → Work → Edition scaffold creation.
    private readonly IMediaEntityChainFactory _chainFactory;

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
        ISidecarWriter             sidecar,
        IMediaEntityChainFactory   chainFactory)
    {
        _watcher       = watcher;
        _debounce      = debounce;
        _hasher        = hasher;
        _processors    = processors;
        _scorer        = scorer;
        _organizer     = organizer;
        _taggers       = taggers;
        _assetRepo     = assetRepo;
        _worker        = worker;
        _publisher     = publisher;
        _options       = options.Value;
        _logger        = logger;
        _claimRepo     = claimRepo;
        _canonicalRepo = canonicalRepo;
        _pipeline      = pipeline;
        _identity      = identity;
        _sidecar       = sidecar;
        _chainFactory  = chainFactory;
    }

    // =========================================================================
    // BackgroundService
    // =========================================================================

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Always wire the event handler so hot-swap from PUT /settings/folders
        // works even if no directory was configured at startup.
        _watcher.FileDetected += (_, evt) => _debounce.Enqueue(evt);

        if (!string.IsNullOrWhiteSpace(_options.WatchDirectory))
        {
            try
            {
                _watcher.AddDirectory(_options.WatchDirectory, _options.IncludeSubdirectories);
                _logger.LogInformation(
                    "IngestionEngine started. Watching: {Path}", _options.WatchDirectory);
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
        // Duplicates are harmless — step 5 (hash check) short-circuits them.
        if (!string.IsNullOrWhiteSpace(_options.WatchDirectory)
            && Directory.Exists(_options.WatchDirectory))
        {
            ScanExistingFiles(_options.WatchDirectory, _options.IncludeSubdirectories);
        }

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
    }

    /// <inheritdoc/>
    async Task IIngestionEngine.StopAsync(CancellationToken ct)
        => await StopAsync(ct).ConfigureAwait(false);

    /// <inheritdoc/>
    void IIngestionEngine.ScanDirectory(string directory, bool includeSubdirectories)
        => ScanExistingFiles(directory, includeSubdirectories);

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
        // Step 2: skip failed probe candidates.
        if (candidate.IsFailed)
        {
            _logger.LogWarning(
                "Ingestion skipped (lock probe failed): {Path} — {Reason}",
                candidate.Path, candidate.FailureReason);
            await SafePublishAsync("IngestionFailed", new IngestionFailedEvent(
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

        await SafePublishAsync("IngestionStarted", new IngestionStartedEvent(
            candidate.Path, DateTimeOffset.UtcNow), ct).ConfigureAwait(false);

        // Step 4: hash.
        var hash = await _hasher.ComputeAsync(candidate.Path, ct).ConfigureAwait(false);

        await SafePublishAsync("IngestionHashed", new IngestionHashedEvent(
            candidate.Path, hash.Hex, hash.FileSize, hash.Elapsed), ct).ConfigureAwait(false);

        // Step 5: duplicate check.
        // If the file is already ingested but still sitting in the Watch Folder
        // (e.g. it scored below the confidence gate on first pass, or LibraryRoot
        // was not configured at that time), attempt to organize it now — metadata
        // may have been enriched by external providers since the initial scan.
        var existing = await _assetRepo.FindByHashAsync(hash.Hex, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            await TryReorganizeExistingAsync(existing, candidate.Path, ct)
                .ConfigureAwait(false);
            return;
        }

        // Step 6: process.
        var result = await _processors.ProcessAsync(candidate.Path, ct).ConfigureAwait(false);

        // Step 7: quarantine corrupt files.
        if (result.IsCorrupt)
        {
            _logger.LogWarning("Corrupt file quarantined: {Path} — {Reason}",
                candidate.Path, result.CorruptReason);
            await SafePublishAsync("IngestionFailed", new IngestionFailedEvent(
                candidate.Path,
                $"Corrupt: {result.CorruptReason}",
                DateTimeOffset.UtcNow), ct).ConfigureAwait(false);
            return;
        }

        // Step 8: convert claims.
        var assetId = Guid.NewGuid();
        var claims  = BuildClaims(assetId, result);

        // Step 9: score.
        var scoringContext = new ScoringContext
        {
            EntityId        = assetId,
            Claims          = claims,
            ProviderWeights = new Dictionary<Guid, double>
                { [LocalProcessorProviderId] = 1.0 },
            Configuration   = new ScoringConfiguration(),
        };

        var scored = await _scorer.ScoreEntityAsync(scoringContext, ct).ConfigureAwait(false);

        // Phase 9: persist claims (append-only; enables re-scoring on weight changes).
        await _claimRepo.InsertBatchAsync(claims, ct).ConfigureAwait(false);

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
        await _canonicalRepo.UpsertBatchAsync(canonicals, ct).ConfigureAwait(false);

        // Enrich the candidate with resolved metadata.
        candidate.Metadata          = BuildMetadataDict(scored);
        candidate.DetectedMediaType = result.DetectedType;

        // Step 9b: create Hub → Work → Edition chain so the FK on media_assets
        // can be satisfied.  The factory reuses an existing Hub when a matching
        // display name is found; otherwise it creates a fresh chain.
        var editionId = await _chainFactory.EnsureEntityChainAsync(
            result.DetectedType,
            candidate.Metadata,
            ct).ConfigureAwait(false);

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

        _logger.LogInformation(
            "Ingested [{Type}] {Path} (hash={Hash})",
            result.DetectedType, candidate.Path, hash.Hex[..12]);

        await SafePublishAsync("IngestionCompleted", new IngestionCompletedEvent(
            candidate.Path,
            result.DetectedType.ToString(),
            DateTimeOffset.UtcNow), ct).ConfigureAwait(false);

        // Phase 9→Pipeline: enqueue non-blocking three-stage hydration pipeline.
        await _pipeline.EnqueueAsync(new HarvestRequest
        {
            EntityId   = assetId,
            EntityType = EntityType.MediaAsset,
            MediaType  = result.DetectedType,
            Hints      = BuildHints(candidate.Metadata),
        }, ct).ConfigureAwait(false);

        // Phase 9: trigger recursive person enrichment for authors/narrators.
        var persons = ExtractPersonReferences(candidate.Metadata);
        if (persons.Count > 0)
            await _identity.EnrichAsync(assetId, persons, ct).ConfigureAwait(false);

        // Step 11: auto-organize.
        // Gate: only organize when we have high-confidence metadata or an explicit
        // user-locked claim. This prevents poorly-tagged files from being committed
        // to the library structure before enough information is available.
        bool hasUserLock    = claims.Any(c => c.IsUserLocked);
        bool highConfidence = scored.OverallConfidence >= 0.85;

        string currentPath = candidate.Path;
        if (_options.AutoOrganize
            && !string.IsNullOrWhiteSpace(_options.LibraryRoot)
            && (highConfidence || hasUserLock))
        {
            var relative = _organizer.CalculatePath(candidate, _options.OrganizationTemplate);
            var destPath = Path.Combine(_options.LibraryRoot, relative,
                                         Path.GetFileName(candidate.Path));

            bool moved = await _organizer.ExecuteMoveAsync(currentPath, destPath, ct)
                                          .ConfigureAwait(false);
            if (moved) currentPath = destPath;
        }

        // Step 11b: write sidecar XML and persist cover art.
        // Only runs when AutoOrganize triggered (same confidence/lock gate) so
        // the sidecar is always co-located with the organized file.
        if (_options.AutoOrganize
            && !string.IsNullOrWhiteSpace(_options.LibraryRoot)
            && (highConfidence || hasUserLock))
        {
            string editionFolder = Path.GetDirectoryName(currentPath) ?? string.Empty;
            string hubFolder     = Path.GetDirectoryName(editionFolder) ?? string.Empty;
            var    meta          = candidate.Metadata
                                   ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Edition-level sidecar — records file identity, user locks, cover path.
            await _sidecar.WriteEditionSidecarAsync(editionFolder, new EditionSidecarData
            {
                Title         = meta.GetValueOrDefault("title"),
                Author        = meta.GetValueOrDefault("author"),
                MediaType     = candidate.DetectedMediaType?.ToString(),
                Isbn          = meta.GetValueOrDefault("isbn"),
                Asin          = meta.GetValueOrDefault("asin"),
                ContentHash   = hash.Hex,
                CoverPath     = "cover.jpg",
                UserLocks     = [],           // empty on first ingest; re-written on resolve
                LastOrganized = DateTimeOffset.UtcNow,
            }, ct).ConfigureAwait(false);

            // Hub-level sidecar — records work identity (idempotent; last ingest wins).
            await _sidecar.WriteHubSidecarAsync(hubFolder, new HubSidecarData
            {
                DisplayName   = meta.GetValueOrDefault("title", "Unknown"),
                Year          = meta.GetValueOrDefault("year"),
                WikidataQid   = meta.GetValueOrDefault("wikidata_qid"),
                Franchise     = meta.GetValueOrDefault("franchise"),
                LastOrganized = DateTimeOffset.UtcNow,
            }, ct).ConfigureAwait(false);

            // Persist cover art as cover.jpg alongside the Edition sidecar.
            // Cover images are NEVER stored in the database — always read from disk.
            if (result.CoverImage is { Length: > 0 })
            {
                await File.WriteAllBytesAsync(
                    Path.Combine(editionFolder, "cover.jpg"),
                    result.CoverImage, ct).ConfigureAwait(false);
            }
        }

        // Step 12: write-back.
        if (_options.WriteBack && candidate.Metadata is not null)
        {
            var tagger = _taggers.FirstOrDefault(t => t.CanHandle(currentPath));
            if (tagger is not null)
            {
                await tagger.WriteTagsAsync(currentPath, candidate.Metadata, ct)
                             .ConfigureAwait(false);

                if (result.CoverImage is { Length: > 0 })
                    await tagger.WriteCoverArtAsync(currentPath, result.CoverImage, ct)
                                 .ConfigureAwait(false);
            }
        }
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

        await SafePublishAsync("MediaRemoved", new
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
            var relative = _organizer.CalculatePath(candidate, _options.OrganizationTemplate);
            var destPath = Path.Combine(_options.LibraryRoot, relative,
                                         Path.GetFileName(filePath));

            ops.Add(new PendingOperation
            {
                SourcePath      = filePath,
                DestinationPath = destPath,
                OperationKind   = "Move",
                Reason          = $"AutoOrganize template: {_options.OrganizationTemplate}",
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

        int count = 0;
        try
        {
            foreach (var filePath in Directory.EnumerateFiles(directory, "*.*", searchOption))
            {
                _debounce.Enqueue(new FileEvent
                {
                    Path       = filePath,
                    EventType  = FileEventType.Created,
                    OccurredAt = DateTimeOffset.UtcNow,
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

        if (count > 0)
            _logger.LogInformation(
                "Initial scan: enqueued {Count} existing file(s) from {Dir}",
                count, directory);
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
        var mediaType = metadata.TryGetValue("media_type", out var mtStr)
            && Enum.TryParse<MediaType>(mtStr, ignoreCase: true, out var mt)
                ? mt
                : (MediaType?)null;

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

        var relative = _organizer.CalculatePath(synth, _options.OrganizationTemplate);
        var destPath = Path.Combine(
            _options.LibraryRoot, relative, Path.GetFileName(currentPath));

        bool moved = await _organizer.ExecuteMoveAsync(currentPath, destPath, ct)
                                      .ConfigureAwait(false);

        if (moved)
        {
            await _assetRepo.UpdateFilePathAsync(existing.Id, destPath, ct)
                            .ConfigureAwait(false);

            _logger.LogInformation(
                "Re-organized existing asset {Hash} → {Dest}",
                existing.ContentHash[..12], destPath);

            await SafePublishAsync("IngestionCompleted", new IngestionCompletedEvent(
                destPath,
                mediaType?.ToString() ?? "Unknown",
                DateTimeOffset.UtcNow), ct).ConfigureAwait(false);
        }
        else
        {
            _logger.LogWarning(
                "Re-organize move failed for {Hash} ({Source} → {Dest})",
                existing.ContentHash[..12], currentPath, destPath);
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
        foreach (var key in new[] { "title", "author", "narrator", "asin", "isbn" })
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

        if (metadata.TryGetValue("author", out var author) &&
            !string.IsNullOrWhiteSpace(author))
            refs.Add(new PersonReference("Author", author.Trim()));

        if (metadata.TryGetValue("narrator", out var narrator) &&
            !string.IsNullOrWhiteSpace(narrator))
            refs.Add(new PersonReference("Narrator", narrator.Trim()));

        return refs;
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
        _logger.LogDebug(ex, "Event publish failed for '{Event}' \xe2\x80\x94 pipeline continues", eventName);
        }
    }
}
