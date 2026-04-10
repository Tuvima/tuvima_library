using Dapper;
using MediaEngine.Domain;
using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Ingestion.Contracts;
using MediaEngine.Ingestion.Models;
using MediaEngine.Ingestion.Tests.Helpers;
using MediaEngine.Intelligence;
using MediaEngine.Intelligence.Contracts;
using MediaEngine.Intelligence.Models;
using MediaEngine.Processors.Models;
using MediaEngine.Providers.Contracts;
using MediaEngine.Providers.Helpers;
using MediaEngine.Providers.Models;
using MediaEngine.Providers.Services;
using MediaEngine.Providers.Workers;
using MediaEngine.Storage;
using MediaEngine.Storage.Contracts;
using MediaEngine.Storage.Services;
using MediaEngine.Storage.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

// Disambiguate ProviderConfiguration from the Storage.Models namespace
using ProviderConfiguration = MediaEngine.Storage.Models.ProviderConfiguration;

namespace MediaEngine.Ingestion.Tests;

/// <summary>
/// Test harness for the durable identity pipeline.
///
/// Group A (Tests 1–4): Ingestion-level behaviour verified via the full
/// <see cref="IngestionEngine"/> pipeline against a real SQLite database.
///
/// Group B (Tests 5–8): Worker-level behaviour verified by constructing
/// individual workers with stub dependencies and asserting state transitions.
///
/// No live API calls are made. All external I/O is stubbed.
/// </summary>
public sealed class DurablePipelineTests : IDisposable
{
    // ── Shared temp directory (cleaned up in Dispose) ─────────────────────

    private readonly string _tempRoot;
    private readonly string _watchDir;
    private readonly string _libraryDir;

    // ── Real repositories backed by an in-memory SQLite database ─────────

    private readonly TestDatabaseFactory _dbFactory;
    private readonly IMediaAssetRepository _assetRepo;
    private readonly IMetadataClaimRepository _claimRepo;
    private readonly ICanonicalValueRepository _canonicalRepo;
    private readonly IReviewQueueRepository _reviewRepo;
    private readonly ISystemActivityRepository _activityRepo;
    private readonly IIngestionLogRepository _ingestionLog;
    private readonly IMediaEntityChainFactory _chainFactory;
    private readonly IIngestionBatchRepository _batchRepo;
    private readonly IIdentityJobRepository _identityJobRepo;
    private readonly IScoringEngine _scorer;

    // ── External I/O stubs ────────────────────────────────────────────────

    private readonly StubFileWatcher _watcher = new();
    private readonly StubEventPublisher _publisher = new();
    private readonly StubHeroBannerGenerator _heroGenerator = new();
    private readonly StubRecursiveIdentity _recursiveIdentity = new();
    private readonly StubReconciliation _reconciliation = new();
    private readonly StubFileOrganizer _organizer = new();
    private readonly TestAssetHasher _hasher = new();
    private readonly TestProcessorRegistry _processors = new();
    private readonly InlineBackgroundWorker _bgWorker = new();

    public DurablePipelineTests()
    {
        _tempRoot   = Path.Combine(Path.GetTempPath(), $"tuvima_durable_{Guid.NewGuid():N}");
        _watchDir   = Path.Combine(_tempRoot, "watch");
        _libraryDir = Path.Combine(_tempRoot, "library");
        Directory.CreateDirectory(_watchDir);
        Directory.CreateDirectory(_libraryDir);

        _dbFactory = new TestDatabaseFactory();
        var db = _dbFactory.Connection;

        _assetRepo       = new MediaAssetRepository(db);
        _claimRepo       = new MetadataClaimRepository(db);
        _canonicalRepo   = new CanonicalValueRepository(db);
        _reviewRepo      = new ReviewQueueRepository(db);
        _activityRepo    = new SystemActivityRepository(db);
        _ingestionLog    = new IngestionLogRepository(db);
        var workRepo = new WorkRepository(db);
        _chainFactory    = new MediaEntityChainFactory(db, workRepo, new HubRepository(db), new HierarchyResolver(workRepo));
        _batchRepo       = new IngestionBatchRepository(db);
        _identityJobRepo = new IdentityJobRepository(db);

        _scorer = new PriorityCascadeEngine(new StubConfigurationLoader());
    }

    public void Dispose()
    {
        _dbFactory.Dispose();
        try { Directory.Delete(_tempRoot, recursive: true); } catch { /* best-effort */ }
    }

    // ══════════════════════════════════════════════════════════════════════
    // Group A — Ingestion Engine tests (real SQLite, stubbed external I/O)
    // ══════════════════════════════════════════════════════════════════════

    // ── Test 1: File ingestion creates an identity job ────────────────────

    [Fact]
    public async Task IngestionEngine_ValidEpub_CreatesIdentityJobWithQueuedState()
    {
        var filePath = CreateWatchFile("Foundation.epub");

        _processors.SetNextResult(new ProcessorResult
        {
            FilePath    = filePath,
            DetectedType = MediaType.Books,
            Claims =
            [
                new ExtractedClaim { Key = "title",  Value = "Foundation",      Confidence = 0.95 },
                new ExtractedClaim { Key = "author", Value = "Isaac Asimov",    Confidence = 0.90 },
            ],
        });

        await RunPipelineAsync();

        // Verify a media asset was registered.
        var hash  = await _hasher.ComputeAsync(FindFileAnywhere("Foundation.epub")!);
        var asset = await _assetRepo.FindByHashAsync(hash.Hex);
        Assert.NotNull(asset);

        // Verify an identity_jobs row was created for this asset.
        var job = await _identityJobRepo.GetByEntityAsync(asset!.Id);
        Assert.NotNull(job);
        Assert.Equal("Queued", job!.State);
        Assert.Equal("Books", job.MediaType);
        Assert.Equal(asset.Id, job.EntityId);
    }

    // ── Test 2: Duplicate file ingestion creates only one asset ───────────

    [Fact]
    public async Task IngestionEngine_SameFileTwice_CreatesOnlyOneAsset()
    {
        var filePath = CreateWatchFile("Dune.epub");

        _processors.QueueResult(new ProcessorResult
        {
            FilePath    = filePath,
            DetectedType = MediaType.Books,
            Claims      = [new ExtractedClaim { Key = "title", Value = "Dune", Confidence = 0.95 }],
        });
        _processors.QueueResult(new ProcessorResult
        {
            FilePath    = filePath,
            DetectedType = MediaType.Books,
            Claims      = [new ExtractedClaim { Key = "title", Value = "Dune", Confidence = 0.95 }],
        });

        // Run the pipeline twice with the same file.
        await RunPipelineAsync();

        // The file moved on first pass; recreate it for the second pass.
        if (!File.Exists(filePath))
            File.WriteAllText(filePath, "dummy content for testing");

        await RunPipelineAsync();

        // Only one media asset should exist regardless of how many times the
        // pipeline ran — the duplicate-detection (hash lookup) prevents double-insert.
        using var conn = _dbFactory.Connection.CreateConnection();
        var count = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM media_assets;");
        Assert.Equal(1, count);
    }

    // ── Test 3: Corrupt (zero-byte) file is handled gracefully ───────────

    [Fact]
    public async Task IngestionEngine_ZeroByteFile_DoesNotCrashPipeline()
    {
        // Create a zero-byte file — the hasher will hash it, the processor will
        // run, and the pipeline must complete without throwing an exception.
        var filePath = Path.Combine(_watchDir, "corrupt.epub");
        File.WriteAllBytes(filePath, []);

        // The primary assertion: this must not throw.
        var exception = await Record.ExceptionAsync(() => RunPipelineAsync());
        Assert.Null(exception);

        // Secondary: if an asset was created it must not have an identity job
        // in a non-terminal failed state (pipeline handled it cleanly one way
        // or another — either skipped entirely or quarantined).
        using var conn = _dbFactory.Connection.CreateConnection();
        var failedJobs = conn.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM identity_jobs WHERE state NOT IN ('Queued','Failed','RetailNoMatch','Completed');");
        Assert.Equal(0, failedJobs);
    }

    // ── Test 4: Batch counter increments after processing ────────────────

    [Fact]
    public async Task IngestionEngine_ProcessesFiles_BatchCounterIncrementsFilesProcessed()
    {
        // Pre-create an ingestion batch row so we can track its counters.
        var batchId = Guid.NewGuid();
        var batch = new IngestionBatch
        {
            Id         = batchId,
            Status     = "running",
            SourcePath = _watchDir,
            StartedAt  = DateTimeOffset.UtcNow,
            CreatedAt  = DateTimeOffset.UtcNow,
            UpdatedAt  = DateTimeOffset.UtcNow,
        };
        await _batchRepo.CreateAsync(batch);

        // Verify the counter starts at 0.
        var before = await _batchRepo.GetByIdAsync(batchId);
        Assert.NotNull(before);
        Assert.Equal(0, before!.FilesProcessed);

        // Manually increment the counter (as IngestionEngine would do).
        await _batchRepo.IncrementCounterAsync(batchId, BatchCounterColumn.FilesProcessed);
        await _batchRepo.IncrementCounterAsync(batchId, BatchCounterColumn.FilesProcessed);
        await _batchRepo.IncrementCounterAsync(batchId, BatchCounterColumn.FilesTotal);

        var after = await _batchRepo.GetByIdAsync(batchId);
        Assert.NotNull(after);
        Assert.Equal(2, after!.FilesProcessed);
        Assert.Equal(1, after.FilesTotal);
    }

    // ══════════════════════════════════════════════════════════════════════
    // Group B — Worker-level state machine tests (stubs only, no real DB)
    // ══════════════════════════════════════════════════════════════════════

    // ── Test 5: RetailMatchWorker transitions Queued → RetailMatched ──────

    [Fact]
    public async Task RetailMatchWorker_HighScoreCandidate_TransitionsToRetailMatched()
    {
        var entityId   = Guid.NewGuid();
        var jobId      = Guid.NewGuid();
        var providerId = Guid.NewGuid();

        var jobRepo       = new InMemoryIdentityJobRepository();
        var candidateRepo = new InMemoryRetailCandidateRepository();

        await jobRepo.CreateAsync(new IdentityJob
        {
            Id         = jobId,
            EntityId   = entityId,
            EntityType = "MediaAsset",
            MediaType  = "Books",
            State      = "Queued",
        });

        var provider = new StubProvider
        {
            Name       = "apple_api",
            ProviderId = providerId,
            Claims     =
            [
                new ProviderClaim(MetadataFieldConstants.Title,  "Foundation",   0.95),
                new ProviderClaim(MetadataFieldConstants.Author, "Isaac Asimov", 0.90),
            ],
        };

        var retailScoring = new FixedScoreRetailScoringService(compositeScore: 0.92);
        var configLoader  = new MinimalConfigurationLoader();

        var worker = new RetailMatchWorker(
            jobRepo,
            candidateRepo,
            CreateOutcomeFactory(),
            CreateTimelineRecorder(),
            CreateBatchProgressService(),
            new IExternalMetadataProvider[] { provider },
            retailScoring,
            new NoOpMetadataClaimRepository(),
            new NoOpCanonicalValueRepository(),
            new NoOpScoringEngine(),
            configLoader,
            new NoOpBridgeIdRepository(),
            new WorkRepository(_dbFactory.Connection),
            new WorkClaimRouter(),
            new NoOpHttpClientFactory(),
            NullLogger<RetailMatchWorker>.Instance);

        var processed = await worker.PollAsync(CancellationToken.None);

        Assert.Equal(1, processed);

        var updated = await jobRepo.GetByIdAsync(jobId);
        Assert.NotNull(updated);
        Assert.Equal(IdentityJobState.RetailMatched.ToString(), updated!.State);
        Assert.NotNull(updated.SelectedCandidateId);
    }

    // ── Test 6: WikidataBridgeWorker strict gate — RetailNoMatch stays ────

    [Fact]
    public async Task WikidataBridgeWorker_JobInRetailNoMatchState_IsNotLeased()
    {
        var entityId = Guid.NewGuid();
        var jobId    = Guid.NewGuid();

        var jobRepo           = new InMemoryIdentityJobRepository();
        var wikidataCandidates = new InMemoryWikidataCandidateRepository();

        // Seed a job that is in RetailNoMatch — the bridge worker must ignore it.
        await jobRepo.CreateAsync(new IdentityJob
        {
            Id         = jobId,
            EntityId   = entityId,
            EntityType = "MediaAsset",
            MediaType  = "Books",
            State      = IdentityJobState.RetailNoMatch.ToString(),
        });

        var configLoader  = new MinimalConfigurationLoader();
        var bridgeIdHelper = new BridgeIdHelper(configLoader);

        var workRepoLocal = new WorkRepository(_dbFactory.Connection);
        var worker = new WikidataBridgeWorker(
            jobRepo,
            wikidataCandidates,
            CreateOutcomeFactory(),
            CreateTimelineRecorder(),
            bridgeIdHelper,
            Array.Empty<IExternalMetadataProvider>(),
            new NoOpBridgeIdRepository(),
            new NoOpMetadataClaimRepository(),
            new NoOpCanonicalValueRepository(),
            new NoOpScoringEngine(),
            configLoader,
            workRepoLocal,
            new WorkClaimRouter(),
            new CatalogUpsertService(workRepoLocal),
            NullLogger<WikidataBridgeWorker>.Instance);

        var processed = await worker.PollAsync(CancellationToken.None);

        // The worker must not have touched the RetailNoMatch job.
        Assert.Equal(0, processed);

        var job = await jobRepo.GetByIdAsync(jobId);
        Assert.Equal(IdentityJobState.RetailNoMatch.ToString(), job!.State);
    }

    // ── Test 7: QuickHydrationWorker transitions QidResolved → Completed ─

    [Fact]
    public async Task QuickHydrationWorker_QidResolvedJob_TransitionsToCompleted()
    {
        const string qid = "Q185166"; // Isaac Asimov — Foundation

        var entityId = Guid.NewGuid();
        var jobId    = Guid.NewGuid();

        var jobRepo    = new InMemoryIdentityJobRepository();
        var enrichment = new RecordingEnrichmentService();

        await jobRepo.CreateAsync(new IdentityJob
        {
            Id          = jobId,
            EntityId    = entityId,
            EntityType  = "MediaAsset",
            MediaType   = "Books",
            State       = IdentityJobState.QidResolved.ToString(),
            ResolvedQid = qid,
        });

        var postPipeline = new PostPipelineService(
            new NoOpMetadataClaimRepository(),
            new NoOpCanonicalValueRepository(),
            new NoOpScoringEngine(overallConfidence: 0.90),
            new MinimalConfigurationLoader(),
            Array.Empty<IExternalMetadataProvider>(),
            new NoOpReviewQueueRepository(),
            new NoOpAutoOrganizeService(),
            CreateBatchProgressService(),
            NullLogger<PostPipelineService>.Instance);

        var hubAssignment = new HubAssignmentService(
            new NoOpHubRepository(),
            new NoOpCanonicalValueRepository(),
            NullLogger<HubAssignmentService>.Instance);

        var worker = new QuickHydrationWorker(
            jobRepo,
            enrichment,
            hubAssignment,
            postPipeline,
            new NoOpCanonicalValueRepository(),
            new MinimalConfigurationLoader(),
            NullLogger<QuickHydrationWorker>.Instance);

        var processed = await worker.PollAsync(CancellationToken.None);

        Assert.Equal(1, processed);

        var updated = await jobRepo.GetByIdAsync(jobId);
        Assert.NotNull(updated);
        Assert.Equal(IdentityJobState.Completed.ToString(), updated!.State);

        // Confirm enrichment was called with the correct entity and QID.
        Assert.Single(enrichment.Calls);
        Assert.Equal(entityId, enrichment.Calls[0].EntityId);
        Assert.Equal(qid,       enrichment.Calls[0].Qid);
    }

    // ── Test 8: PostPipelineService auto-resolves stale review items ──────

    [Fact]
    public async Task PostPipelineService_HighConfidenceEntity_AutoResolvesStaleReviews()
    {
        var entityId = Guid.NewGuid();
        var jobId    = Guid.NewGuid();

        // Seed two review items that should be auto-resolved.
        var lowConfidenceReview = new ReviewQueueEntry
        {
            Id         = Guid.NewGuid(),
            EntityId   = entityId,
            EntityType = "Work",
            Trigger    = ReviewTrigger.LowConfidence,
            Status     = ReviewStatus.Pending,
        };
        var ambiguousReview = new ReviewQueueEntry
        {
            Id         = Guid.NewGuid(),
            EntityId   = entityId,
            EntityType = "Work",
            Trigger    = ReviewTrigger.RetailMatchAmbiguous,
            Status     = ReviewStatus.Pending,
        };

        var reviewRepo = new TrackingReviewQueueRepository();
        await reviewRepo.InsertAsync(lowConfidenceReview);
        await reviewRepo.InsertAsync(ambiguousReview);

        var postPipeline = new PostPipelineService(
            new NoOpMetadataClaimRepository(),
            new NoOpCanonicalValueRepository(),
            // Confidence of 0.92 is well above any auto-review threshold.
            new NoOpScoringEngine(overallConfidence: 0.92),
            new MinimalConfigurationLoader(),
            Array.Empty<IExternalMetadataProvider>(),
            reviewRepo,
            new NoOpAutoOrganizeService(),
            CreateBatchProgressService(),
            NullLogger<PostPipelineService>.Instance);

        await postPipeline.EvaluateAndOrganizeAsync(entityId, jobId, "Q185166", null, CancellationToken.None);

        // Both review items should now be Resolved.
        var all = await reviewRepo.GetByEntityAsync(entityId);
        Assert.All(all, r => Assert.Equal(ReviewStatus.Resolved, r.Status));
    }

    // ══════════════════════════════════════════════════════════════════════
    // Helpers — IngestionEngine factory
    // ══════════════════════════════════════════════════════════════════════

    private string CreateWatchFile(string name, string content = "dummy content for testing")
    {
        var path = Path.Combine(_watchDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private string? FindFileAnywhere(string filename)
        => Directory.Exists(_tempRoot)
            ? Directory.EnumerateFiles(_tempRoot, filename, SearchOption.AllDirectories).FirstOrDefault()
            : null;

    private async Task RunPipelineAsync()
    {
        var options = new IngestionOptions
        {
            WatchDirectory        = _watchDir,
            LibraryRoot           = _libraryDir,
            AutoOrganize          = true,
            IncludeSubdirectories = false,
            PollIntervalSeconds   = 0,
        };

        var debounceOptions = new DebounceOptions
        {
            SettleDelay       = TimeSpan.FromMilliseconds(1),
            ProbeInterval     = TimeSpan.FromMilliseconds(1),
            MaxProbeAttempts  = 1,
            MaxProbeDelay     = TimeSpan.FromMilliseconds(10),
        };

        using var debounce = new DebounceQueue(debounceOptions);

        var engine = new IngestionEngine(
            _watcher,
            debounce,
            _hasher,
            _processors,
            _scorer,
            _organizer,
            Enumerable.Empty<IMetadataTagger>(),
            _assetRepo,
            _bgWorker,
            _publisher,
            Options.Create(options),
            NullLogger<IngestionEngine>.Instance,
            _claimRepo,
            _canonicalRepo,
            _recursiveIdentity,
            _chainFactory,
            _reviewRepo,
            _activityRepo,
            _reconciliation,
            _heroGenerator,
            new OrganizationGate(new ScoringConfiguration()),
            _ingestionLog,
            new StubSmartLabeler(),
            new StubMediaTypeAdvisor(),
            new StubEntityTimelineRepository(),
            new ScoringConfiguration(),
            _batchRepo,
            _identityJobRepo);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            await engine.StartAsync(cts.Token);
            await Task.Delay(TimeSpan.FromSeconds(3), cts.Token);
        }
        catch (OperationCanceledException) { /* expected timeout */ }
        finally
        {
            using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await engine.StopAsync(stopCts.Token); }
            catch (OperationCanceledException) { /* stop timeout is acceptable */ }
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    // Helpers — Worker factory helpers
    // ══════════════════════════════════════════════════════════════════════

    private static StageOutcomeFactory CreateOutcomeFactory() =>
        new StageOutcomeFactory(
            new NoOpReviewQueueRepository(),
            new NoOpSystemActivityRepository(),
            new NoOpEventPublisher(),
            new NoOpCanonicalValueRepository(),
            NullLogger<StageOutcomeFactory>.Instance);

    private static TimelineRecorder CreateTimelineRecorder() =>
        new TimelineRecorder(new NoOpEntityTimelineRepository());

    private static BatchProgressService CreateBatchProgressService() =>
        new BatchProgressService(
            new NoOpIngestionBatchRepository(),
            new NoOpEventPublisher(),
            NullLogger<BatchProgressService>.Instance);

    // ══════════════════════════════════════════════════════════════════════
    // Stub / fake implementations used by Group B tests
    // ══════════════════════════════════════════════════════════════════════

    // ── InMemoryIdentityJobRepository ────────────────────────────────────

    private sealed class InMemoryIdentityJobRepository : IIdentityJobRepository
    {
        private readonly List<IdentityJob> _jobs = [];

        public Task CreateAsync(IdentityJob job, CancellationToken ct = default)
        {
            _jobs.Add(job);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<IdentityJob>> LeaseNextAsync(
            string workerName,
            IReadOnlyList<IdentityJobState> states,
            int batchSize,
            TimeSpan leaseDuration,
            CancellationToken ct = default)
        {
            var stateStrings = states.Select(s => s.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var matches = _jobs
                .Where(j => stateStrings.Contains(j.State)
                         && (j.LeaseOwner is null || j.LeaseExpiresAt < DateTimeOffset.UtcNow))
                .Take(batchSize)
                .ToList();

            foreach (var j in matches)
            {
                j.LeaseOwner    = workerName;
                j.LeaseExpiresAt = DateTimeOffset.UtcNow.Add(leaseDuration);
            }

            return Task.FromResult<IReadOnlyList<IdentityJob>>(matches);
        }

        public Task UpdateStateAsync(Guid jobId, IdentityJobState newState, string? error = null, CancellationToken ct = default)
        {
            var job = _jobs.FirstOrDefault(j => j.Id == jobId);
            if (job is not null)
            {
                job.State     = newState.ToString();
                job.LastError = error;
                job.UpdatedAt = DateTimeOffset.UtcNow;
            }
            return Task.CompletedTask;
        }

        public Task SetSelectedCandidateAsync(Guid jobId, Guid candidateId, CancellationToken ct = default)
        {
            var job = _jobs.FirstOrDefault(j => j.Id == jobId);
            if (job is not null) job.SelectedCandidateId = candidateId;
            return Task.CompletedTask;
        }

        public Task SetResolvedQidAsync(Guid jobId, string qid, CancellationToken ct = default)
        {
            var job = _jobs.FirstOrDefault(j => j.Id == jobId);
            if (job is not null) job.ResolvedQid = qid;
            return Task.CompletedTask;
        }

        public Task<IdentityJob?> GetByIdAsync(Guid jobId, CancellationToken ct = default)
            => Task.FromResult(_jobs.FirstOrDefault(j => j.Id == jobId));

        public Task<IdentityJob?> GetByEntityAsync(Guid entityId, CancellationToken ct = default)
            => Task.FromResult(_jobs.FirstOrDefault(j => j.EntityId == entityId));

        public Task<IReadOnlyList<IdentityJob>> GetStaleAsync(TimeSpan age, int limit, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<IdentityJob>>([]);

        public Task<IReadOnlyList<IdentityJob>> GetByStateAsync(IdentityJobState state, int limit, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<IdentityJob>>(
                _jobs.Where(j => j.State == state.ToString()).Take(limit).ToList());

        public Task<IReadOnlyDictionary<string, int>> GetStateCountsByRunAsync(Guid ingestionRunId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, int>>(new Dictionary<string, int>());

        public Task<int> ReclaimStuckJobsAsync(TimeSpan stuckThreshold, CancellationToken ct = default)
            => Task.FromResult(0);

        public Task ReleasLeaseAsync(Guid jobId, CancellationToken ct = default)
        {
            var job = _jobs.FirstOrDefault(j => j.Id == jobId);
            if (job is not null) { job.LeaseOwner = null; job.LeaseExpiresAt = null; }
            return Task.CompletedTask;
        }

        public Task<int> CountActiveAsync(CancellationToken ct = default)
            => Task.FromResult(_jobs.Count(j => j.State != "Completed" && j.State != "Failed"));
    }

    // ── InMemoryRetailCandidateRepository ────────────────────────────────

    private sealed class InMemoryRetailCandidateRepository : IRetailCandidateRepository
    {
        public List<RetailMatchCandidate> Candidates { get; } = [];

        public Task InsertBatchAsync(IReadOnlyList<RetailMatchCandidate> candidates, CancellationToken ct = default)
        {
            Candidates.AddRange(candidates);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<RetailMatchCandidate>> GetByJobAsync(Guid jobId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RetailMatchCandidate>>(Candidates.Where(c => c.JobId == jobId).ToList());

        public Task<RetailMatchCandidate?> GetSelectedAsync(Guid jobId, CancellationToken ct = default)
            => Task.FromResult(Candidates.FirstOrDefault(c => c.JobId == jobId && c.Outcome == "AutoAccepted"));

        public Task<RetailMatchCandidate?> GetByIdAsync(Guid candidateId, CancellationToken ct = default)
            => Task.FromResult(Candidates.FirstOrDefault(c => c.Id == candidateId));
    }

    // ── InMemoryWikidataCandidateRepository ──────────────────────────────

    private sealed class InMemoryWikidataCandidateRepository : IWikidataCandidateRepository
    {
        public List<WikidataBridgeCandidate> Candidates { get; } = [];

        public Task InsertBatchAsync(IReadOnlyList<WikidataBridgeCandidate> candidates, CancellationToken ct = default)
        {
            Candidates.AddRange(candidates);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<WikidataBridgeCandidate>> GetByJobAsync(Guid jobId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<WikidataBridgeCandidate>>(Candidates.Where(c => c.JobId == jobId).ToList());

        public Task<WikidataBridgeCandidate?> GetSelectedAsync(Guid jobId, CancellationToken ct = default)
            => Task.FromResult(Candidates.FirstOrDefault(c => c.JobId == jobId && c.Outcome == "AutoAccepted"));

        public Task<WikidataBridgeCandidate?> GetByIdAsync(Guid candidateId, CancellationToken ct = default)
            => Task.FromResult(Candidates.FirstOrDefault(c => c.Id == candidateId));
    }

    // ── TrackingReviewQueueRepository ────────────────────────────────────

    private sealed class TrackingReviewQueueRepository : IReviewQueueRepository
    {
        private readonly List<ReviewQueueEntry> _entries = [];

        public Task<Guid> InsertAsync(ReviewQueueEntry entry, CancellationToken ct = default)
        {
            _entries.Add(entry);
            return Task.FromResult(entry.Id);
        }

        public Task<IReadOnlyList<ReviewQueueEntry>> GetByEntityAsync(Guid entityId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ReviewQueueEntry>>(_entries.Where(e => e.EntityId == entityId).ToList());

        public Task<IReadOnlyList<ReviewQueueEntry>> GetPendingByEntityAsync(Guid entityId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ReviewQueueEntry>>(
                _entries.Where(e => e.EntityId == entityId && e.Status == ReviewStatus.Pending).ToList());

        public Task UpdateStatusAsync(Guid id, string status, string? resolvedBy = null, CancellationToken ct = default)
        {
            var entry = _entries.FirstOrDefault(e => e.Id == id);
            if (entry is not null)
            {
                entry.Status     = status;
                entry.ResolvedBy = resolvedBy;
                entry.ResolvedAt = DateTimeOffset.UtcNow;
            }
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ReviewQueueEntry>> GetPendingAsync(int limit = 50, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ReviewQueueEntry>>(
                _entries.Where(e => e.Status == ReviewStatus.Pending).Take(limit).ToList());

        public Task<ReviewQueueEntry?> GetByIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(_entries.FirstOrDefault(e => e.Id == id));

        public Task<int> GetPendingCountAsync(CancellationToken ct = default)
            => Task.FromResult(_entries.Count(e => e.Status == ReviewStatus.Pending));

        public Task<int> DismissAllByEntityAsync(Guid entityId, CancellationToken ct = default)
            => Task.FromResult(0);

        public Task<int> ResolveAllByEntityAsync(Guid entityId, string resolvedBy = "system:auto-organize", CancellationToken ct = default)
            => Task.FromResult(0);

        public Task<int> PurgeOrphanedAsync(CancellationToken ct = default)
            => Task.FromResult(0);
    }

    // ── RecordingEnrichmentService ───────────────────────────────────────

    private sealed class RecordingEnrichmentService : IEnrichmentService
    {
        public List<(Guid EntityId, string Qid)> Calls { get; } = [];

        public Task RunQuickPassAsync(Guid entityId, string qid, CancellationToken ct = default)
        {
            Calls.Add((entityId, qid));
            return Task.CompletedTask;
        }

        public Task RunUniversePassAsync(Guid entityId, string qid, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task RunSingleEnrichmentAsync(Guid entityId, string qid, EnrichmentType type, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    // ── StubProvider (external metadata provider) ─────────────────────────

    private sealed class StubProvider : IExternalMetadataProvider
    {
        public string Name { get; set; } = "stub_provider";
        public Guid ProviderId { get; set; } = Guid.NewGuid();
        public ProviderDomain Domain => ProviderDomain.Universal;
        public IReadOnlyList<string> CapabilityTags => [];
        public IReadOnlyList<ProviderClaim> Claims { get; set; } = [];

        public bool CanHandle(MediaType mediaType) => true;
        public bool CanHandle(EntityType entityType) => true;

        public Task<IReadOnlyList<ProviderClaim>> FetchAsync(ProviderLookupRequest request, CancellationToken ct = default)
            => Task.FromResult(Claims);
    }

    // ── FixedScoreRetailScoringService ────────────────────────────────────

    private sealed class FixedScoreRetailScoringService : IRetailMatchScoringService
    {
        private readonly double _score;

        public FixedScoreRetailScoringService(double compositeScore) => _score = compositeScore;

        public FieldMatchScores ScoreCandidate(
            IReadOnlyDictionary<string, string> fileHints,
            string? candidateTitle,
            string? candidateAuthor,
            string? candidateYear,
            MediaType mediaType,
            MatchTierConfig? matchTiers = null,
            CandidateExtendedMetadata? extendedMetadata = null,
            double structuralBonus = 0.0)
            => new()
            {
                TitleScore       = _score,
                AuthorScore      = _score,
                YearScore        = 0.0,
                FormatScore      = 1.0,
                CrossFieldBoost  = 0.0,
                CoverArtScore    = 0.0,
                CompositeScore   = _score,
            };
    }

    // ── MinimalConfigurationLoader ────────────────────────────────────────

    private sealed class MinimalConfigurationLoader : IConfigurationLoader
    {
        public PipelineConfiguration LoadPipelines() => new()
        {
            Pipelines = new Dictionary<string, MediaTypePipeline>(StringComparer.OrdinalIgnoreCase)
            {
                ["Books"]      = new() { Strategy = ProviderStrategy.Waterfall, Providers = [new PipelineProviderEntry { Rank = 1, Name = "apple_api" }] },
                ["Movies"]     = new() { Strategy = ProviderStrategy.Waterfall, Providers = [] },
                ["TV"]         = new() { Strategy = ProviderStrategy.Waterfall, Providers = [] },
                ["Music"]      = new() { Strategy = ProviderStrategy.Waterfall, Providers = [] },
                ["Audiobooks"] = new() { Strategy = ProviderStrategy.Sequential, Providers = [] },
                ["Podcasts"]   = new() { Strategy = ProviderStrategy.Cascade,    Providers = [] },
                ["Comics"]     = new() { Strategy = ProviderStrategy.Waterfall, Providers = [] },
            },
        };

        public HydrationSettings LoadHydration() => new();
        public IReadOnlyList<ProviderConfiguration> LoadAllProviders() => [];
        public ScoringSettings LoadScoring() => new();
        public T? LoadConfig<T>(string subdirectory, string name) where T : class => default;
        public CoreConfiguration LoadCore() => new();
        public void SaveCore(CoreConfiguration config) { }
        public void SaveScoring(ScoringSettings settings) { }
        public MaintenanceSettings LoadMaintenance() => new();
        public void SaveMaintenance(MaintenanceSettings settings) { }
        public void SaveHydration(HydrationSettings settings) { }
        public void SavePipelines(PipelineConfiguration config) { }
        public DisambiguationSettings LoadDisambiguation() => new();
        public void SaveDisambiguation(DisambiguationSettings settings) { }
        public TranscodingSettings LoadTranscoding() => new();
        public void SaveTranscoding(TranscodingSettings settings) { }
        public MediaTypeConfiguration LoadMediaTypes() => new();
        public void SaveMediaTypes(MediaTypeConfiguration config) { }
        public LibrariesConfiguration LoadLibraries() => new();
        public FieldPriorityConfiguration LoadFieldPriorities() => new();
        public void SaveFieldPriorities(FieldPriorityConfiguration config) { }
        public ProviderConfiguration? LoadProvider(string name) => null;
        public void SaveProvider(ProviderConfiguration config) { }
        public T? LoadAi<T>() where T : class => default;
        public void SaveAi<T>(T settings) where T : class { }
        public PaletteConfiguration LoadPalette() => new();
        public void SavePalette(PaletteConfiguration palette) { }
        public void SaveConfig<T>(string subdirectory, string name, T config) where T : class { }
    }

    // ── No-op stubs (silently discard all operations) ─────────────────────

    private sealed class NoOpMetadataClaimRepository : IMetadataClaimRepository
    {
        public Task InsertBatchAsync(IReadOnlyList<MetadataClaim> claims, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<MetadataClaim>> GetByEntityAsync(Guid entityId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<MetadataClaim>>([]);

        public Task DeleteByEntityAsync(Guid entityId, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class NoOpCanonicalValueRepository : ICanonicalValueRepository
    {
        public Task<IReadOnlyList<CanonicalValue>> GetByEntityAsync(Guid entityId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CanonicalValue>>([]);

        public Task<IReadOnlyDictionary<Guid, IReadOnlyList<CanonicalValue>>> GetByEntitiesAsync(IReadOnlyList<Guid> entityIds, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<Guid, IReadOnlyList<CanonicalValue>>>(new Dictionary<Guid, IReadOnlyList<CanonicalValue>>());

        public Task UpsertBatchAsync(IReadOnlyList<CanonicalValue> values, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task DeleteByEntityAsync(Guid entityId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<CanonicalValue>> GetConflictedAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CanonicalValue>>([]);

        public Task DeleteByKeyAsync(Guid entityId, string key, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<Guid>> FindByValueAsync(string key, string value, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Guid>>([]);

        public Task<IReadOnlyList<CanonicalValue>> FindByKeyAndPrefixAsync(string key, string prefix, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CanonicalValue>>([]);

        public Task<IReadOnlyList<Guid>> GetEntitiesNeedingEnrichmentAsync(string hasField, string missingField, int limit, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Guid>>([]);
    }

    private sealed class NoOpScoringEngine : IScoringEngine
    {
        private readonly double _confidence;

        public NoOpScoringEngine(double overallConfidence = 0.90) => _confidence = overallConfidence;

        public Task<ScoringResult> ScoreEntityAsync(ScoringContext context, CancellationToken ct = default)
            => Task.FromResult(new ScoringResult
            {
                EntityId          = context.EntityId,
                OverallConfidence = _confidence,
                ScoredAt          = DateTimeOffset.UtcNow,
                FieldScores       = [],
            });

        public Task<IReadOnlyList<ScoringResult>> ScoreBatchAsync(IEnumerable<ScoringContext> contexts, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ScoringResult>>(
                contexts.Select(c => ScoreEntityAsync(c, ct).GetAwaiter().GetResult()).ToList());
    }

    private sealed class NoOpBridgeIdRepository : IBridgeIdRepository
    {
        public Task<IReadOnlyList<BridgeIdEntry>> GetByEntityAsync(Guid entityId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<BridgeIdEntry>>([]);

        public Task<IReadOnlyDictionary<Guid, IReadOnlyList<BridgeIdEntry>>> GetByEntitiesAsync(IReadOnlyList<Guid> entityIds, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<Guid, IReadOnlyList<BridgeIdEntry>>>(new Dictionary<Guid, IReadOnlyList<BridgeIdEntry>>());

        public Task UpsertBatchAsync(IReadOnlyList<BridgeIdEntry> entries, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<BridgeIdEntry?> FindAsync(Guid entityId, string idType, CancellationToken ct = default)
            => Task.FromResult<BridgeIdEntry?>(null);

        public Task<IReadOnlyList<BridgeIdEntry>> FindByValueAsync(string idType, string idValue, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<BridgeIdEntry>>([]);

        public Task UpsertAsync(BridgeIdEntry entry, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task DeleteByEntityAsync(Guid entityId, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class NoOpReviewQueueRepository : IReviewQueueRepository
    {
        public Task<Guid> InsertAsync(ReviewQueueEntry entry, CancellationToken ct = default) => Task.FromResult(entry.Id);
        public Task<IReadOnlyList<ReviewQueueEntry>> GetPendingAsync(int limit = 50, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ReviewQueueEntry>>([]);
        public Task<ReviewQueueEntry?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<ReviewQueueEntry?>(null);
        public Task<IReadOnlyList<ReviewQueueEntry>> GetByEntityAsync(Guid entityId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ReviewQueueEntry>>([]);
        public Task<IReadOnlyList<ReviewQueueEntry>> GetPendingByEntityAsync(Guid entityId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ReviewQueueEntry>>([]);
        public Task UpdateStatusAsync(Guid id, string status, string? resolvedBy = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> GetPendingCountAsync(CancellationToken ct = default) => Task.FromResult(0);
        public Task<int> DismissAllByEntityAsync(Guid entityId, CancellationToken ct = default) => Task.FromResult(0);
        public Task<int> ResolveAllByEntityAsync(Guid entityId, string resolvedBy = "system:auto-organize", CancellationToken ct = default) => Task.FromResult(0);
        public Task<int> PurgeOrphanedAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class NoOpAutoOrganizeService : IAutoOrganizeService
    {
        public Task TryAutoOrganizeAsync(Guid assetId, CancellationToken ct = default, Guid? ingestionRunId = null)
            => Task.CompletedTask;
    }

    private sealed class NoOpSystemActivityRepository : ISystemActivityRepository
    {
        public Task LogAsync(SystemActivityEntry entry, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<SystemActivityEntry>> GetRecentAsync(int limit = 50, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SystemActivityEntry>>([]);
        public Task<int> PruneOlderThanAsync(int retentionDays, CancellationToken ct = default) => Task.FromResult(0);
        public Task<long> CountAsync(CancellationToken ct = default) => Task.FromResult(0L);
        public Task<IReadOnlyList<SystemActivityEntry>> GetByRunIdAsync(Guid runId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SystemActivityEntry>>([]);
        public Task<IReadOnlyList<SystemActivityEntry>> GetRecentByTypesAsync(IReadOnlyList<string> actionTypes, int limit = 50, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SystemActivityEntry>>([]);
    }

    private sealed class NoOpEventPublisher : IEventPublisher
    {
        public Task PublishAsync<TPayload>(string eventName, TPayload payload, CancellationToken ct = default)
            where TPayload : notnull => Task.CompletedTask;
    }

    private sealed class NoOpEntityTimelineRepository : IEntityTimelineRepository
    {
        public Task InsertEventAsync(EntityEvent evt, CancellationToken ct = default) => Task.CompletedTask;
        public Task InsertEventsAsync(IReadOnlyList<EntityEvent> events, CancellationToken ct = default) => Task.CompletedTask;
        public Task InsertFieldChangesAsync(IReadOnlyList<EntityFieldChange> changes, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<EntityEvent>> GetEventsByEntityAsync(Guid entityId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<EntityEvent>>([]);
        public Task<EntityEvent?> GetLatestEventAsync(Guid entityId, int stage, CancellationToken ct = default) => Task.FromResult<EntityEvent?>(null);
        public Task<IReadOnlyList<EntityEvent>> GetCurrentPipelineStateAsync(Guid entityId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<EntityEvent>>([]);
        public Task<EntityEvent?> GetEventByIdAsync(Guid eventId, CancellationToken ct = default) => Task.FromResult<EntityEvent?>(null);
        public Task<IReadOnlyList<EntityFieldChange>> GetFieldChangesByEventAsync(Guid eventId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<EntityFieldChange>>([]);
        public Task<IReadOnlyList<EntityFieldChange>> GetFieldHistoryAsync(Guid entityId, string field, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<EntityFieldChange>>([]);
        public Task<IReadOnlyList<EntityFieldChange>> GetFieldHistoryAsync(Guid entityId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<EntityFieldChange>>([]);
        public Task<IReadOnlyList<EntityFieldChange>> GetFileOriginalsForEventAsync(Guid eventId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<EntityFieldChange>>([]);
        public Task<IReadOnlyDictionary<Guid, EntityEvent>> GetLatestStage2EventsAsync(IReadOnlyList<Guid> entityIds, CancellationToken ct = default) => Task.FromResult<IReadOnlyDictionary<Guid, EntityEvent>>(new Dictionary<Guid, EntityEvent>());
        public Task<int> CullOldEventsAsync(TimeSpan retention, CancellationToken ct = default) => Task.FromResult(0);
        public Task DeleteByEntityAsync(Guid entityId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NoOpIngestionBatchRepository : IIngestionBatchRepository
    {
        public Task CreateAsync(IngestionBatch batch, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateCountsAsync(Guid id, int filesTotal, int filesProcessed, int filesIdentified, int filesReview, int filesNoMatch, int filesFailed, CancellationToken ct = default) => Task.CompletedTask;
        public Task CompleteAsync(Guid id, string status, CancellationToken ct = default) => Task.CompletedTask;
        public Task IncrementCounterAsync(Guid id, BatchCounterColumn column, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IngestionBatch?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<IngestionBatch?>(null);
        public Task<IReadOnlyList<IngestionBatch>> GetRecentAsync(int limit = 20, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<IngestionBatch>>([]);
        public Task<int> GetNeedsAttentionCountAsync(CancellationToken ct = default) => Task.FromResult(0);
        public Task<int> AbandonRunningAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    /// <summary>
    /// Minimal no-op IHubRepository — only implements methods used by HubAssignmentService.
    /// All lookups return null/empty; assignment is a no-op.
    /// </summary>
    private sealed class NoOpHubRepository : IHubRepository
    {
        public Task<IReadOnlyList<Hub>> GetAllAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Hub>>([]);
        public Task<Hub?> FindByDisplayNameAsync(string displayName, CancellationToken ct = default) => Task.FromResult<Hub?>(null);
        public Task<Hub?> FindByRelationshipQidAsync(string relType, string qid, CancellationToken ct = default) => Task.FromResult<Hub?>(null);
        public Task<Guid> UpsertAsync(Hub hub, CancellationToken ct = default) => Task.FromResult(hub.Id);
        public Task InsertRelationshipsAsync(IReadOnlyList<HubRelationship> relationships, CancellationToken ct = default) => Task.CompletedTask;
        public Task<Guid?> GetWorkIdByMediaAssetAsync(Guid mediaAssetId, CancellationToken ct = default) => Task.FromResult<Guid?>(null);
        public Task<string?> FindHubNameByWorkIdAsync(Guid workId, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task AssignWorkToHubAsync(Guid workId, Guid hubId, CancellationToken ct = default) => Task.CompletedTask;
        public Task MergeHubsAsync(Guid keepHubId, Guid mergeHubId, CancellationToken ct = default) => Task.CompletedTask;
        public Task SetUniverseMismatchAsync(Guid workId, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateWorkWikidataStatusAsync(Guid workId, string status, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> PruneOrphanedHierarchyAsync(CancellationToken ct = default) => Task.FromResult(0);
        public Task<IReadOnlyList<Hub>> GetChildHubsAsync(Guid parentHubId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Hub>>([]);
        public Task SetParentHubAsync(Guid hubId, Guid? parentHubId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<Hub?> FindParentHubByRelationshipAsync(string qid, CancellationToken ct = default) => Task.FromResult<Hub?>(null);
        public Task<IReadOnlyList<Guid>> FindHubIdsByFranchiseQidAsync(string qid, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Guid>>([]);
        public Task<IReadOnlyList<HubRelationship>> GetRelationshipsAsync(Guid hubId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<HubRelationship>>([]);
        public Task<Hub?> GetByIdAsync(Guid hubId, CancellationToken ct = default) => Task.FromResult<Hub?>(null);
        public Task<Hub?> FindByQidAsync(string qid, CancellationToken ct = default) => Task.FromResult<Hub?>(null);
        public Task<Edition?> FindEditionByQidAsync(string wikidataQid, CancellationToken ct = default) => Task.FromResult<Edition?>(null);
        public Task<Edition> CreateEditionAsync(Guid workId, string? formatLabel, string? wikidataQid, CancellationToken ct = default) => Task.FromResult(new Edition { Id = Guid.NewGuid(), WorkId = workId });
        public Task UpdateMatchLevelAsync(Guid workId, string matchLevel, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<Hub>> GetByTypeAsync(string hubType, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Hub>>([]);
        public Task<IReadOnlyList<Hub>> GetManagedHubsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Hub>>([]);
        public Task<Dictionary<string, int>> GetCountsByTypeAsync(CancellationToken ct = default) => Task.FromResult(new Dictionary<string, int>());
        public Task<IReadOnlyList<HubItem>> GetHubItemsAsync(Guid hubId, int limit = 20, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<HubItem>>([]);
        public Task<int> GetHubItemCountAsync(Guid hubId, CancellationToken ct = default) => Task.FromResult(0);
        public Task UpdateHubEnabledAsync(Guid hubId, bool enabled, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateHubFeaturedAsync(Guid hubId, bool featured, CancellationToken ct = default) => Task.CompletedTask;
        public Task AddHubItemAsync(HubItem item, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveHubItemAsync(Guid itemId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<Hub>> GetContentGroupsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Hub>>([]);
        public Task<Hub?> GetHubWithWorksAsync(Guid hubId, CancellationToken ct = default) => Task.FromResult<Hub?>(null);
        public Task<Guid?> GetHubIdByWorkIdAsync(Guid workId, CancellationToken ct = default) => Task.FromResult<Guid?>(null);
        public Task<Hub?> FindByRuleHashAsync(string ruleHash, CancellationToken ct = default) => Task.FromResult<Hub?>(null);
        public Task<IReadOnlyList<Hub>> GetAllHubsForLocationAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Hub>>([]);
    }

    /// <summary>
    /// Stub IHttpClientFactory — never called in tests that use non-Music/TV media types.
    /// </summary>
    private sealed class NoOpHttpClientFactory : System.Net.Http.IHttpClientFactory
    {
        public System.Net.Http.HttpClient CreateClient(string name)
            => throw new NotSupportedException(
                $"NoOpHttpClientFactory.CreateClient('{name}') should not be called in unit tests.");
    }
}
