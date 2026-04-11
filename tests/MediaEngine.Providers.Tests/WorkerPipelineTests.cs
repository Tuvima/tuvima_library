using MediaEngine.Domain;
using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Intelligence.Contracts;
using MediaEngine.Intelligence.Models;
using MediaEngine.Providers.Contracts;
using MediaEngine.Providers.Helpers;
using MediaEngine.Providers.Models;
using MediaEngine.Providers.Services;
using MediaEngine.Providers.Workers;
using MediaEngine.Storage.Contracts;
using MediaEngine.Storage.Models;
using MediaEngine.Storage.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

// Disambiguate ProviderConfiguration — the IConfigurationLoader uses the Storage.Models one
using ProviderConfiguration = MediaEngine.Storage.Models.ProviderConfiguration;

namespace MediaEngine.Providers.Tests;

public sealed class WorkerPipelineTests
{
    // ── Test 1: RetailMatchWorker auto-accepts when composite score ≥ 0.85 ──

    [Fact]
    public async Task RetailMatchWorker_AutoAccepted_TransitionsToRetailMatched()
    {
        var entityId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var providerId = Guid.NewGuid();

        var jobRepo = new StubIdentityJobRepository();
        var candidateRepo = new StubRetailCandidateRepository();

        var job = new IdentityJob
        {
            Id = jobId,
            EntityId = entityId,
            EntityType = "MediaAsset",
            MediaType = "Books",
            State = "Queued",
        };
        await jobRepo.CreateAsync(job);

        var provider = new StubExternalMetadataProvider
        {
            Name = "apple_api",
            ProviderId = providerId,
            Claims =
            [
                new ProviderClaim(MetadataFieldConstants.Title, "Dune", 0.95),
                new ProviderClaim(MetadataFieldConstants.Author, "Frank Herbert", 0.95),
            ],
        };

        var retailScoring = new StubRetailMatchScoringService
        {
            Result = new FieldMatchScores
            {
                TitleScore = 0.95,
                AuthorScore = 0.90,
                YearScore = 0.0,
                FormatScore = 1.0,
                CrossFieldBoost = 0.0,
                CoverArtScore = 0.0,
                CompositeScore = 0.90,
            },
        };

        var configLoader = new StubConfigurationLoader();
        var canonicalRepo = new StubCanonicalValueRepository();
        var claimRepo = new StubMetadataClaimRepository();
        var bridgeIdRepo = new StubBridgeIdRepository();
        var scoringEngine = new StubScoringEngine();
        var outcomeFactory = CreateStubStageOutcomeFactory();
        var timeline = CreateStubTimelineRecorder();
        var batchProgress = CreateStubBatchProgressService();

        var worker = new RetailMatchWorker(
            jobRepo,
            candidateRepo,
            outcomeFactory,
            timeline,
            batchProgress,
            new[] { provider },
            retailScoring,
            claimRepo,
            canonicalRepo,
            scoringEngine,
            configLoader,
            bridgeIdRepo,
            new StubWorkRepository(),
            new WorkClaimRouter(),
            new StubHttpClientFactory(),
            null!, // PostPipelineService — not exercised in this test
            NullLogger<RetailMatchWorker>.Instance);

        var processed = await worker.PollAsync(CancellationToken.None);

        Assert.Equal(1, processed);

        var updatedJob = await jobRepo.GetByIdAsync(jobId);
        Assert.NotNull(updatedJob);
        Assert.Equal(IdentityJobState.RetailMatched.ToString(), updatedJob!.State);

        Assert.Single(candidateRepo.Candidates);
        Assert.Equal("AutoAccepted", candidateRepo.Candidates[0].Outcome);
    }

    // ── Test 2: RetailMatchWorker no match → RetailNoMatch, WikidataBridge skips ──

    [Fact]
    public async Task RetailMatchWorker_NoMatch_TransitionsToRetailNoMatch_WikidataSkips()
    {
        var entityId = Guid.NewGuid();
        var jobId = Guid.NewGuid();

        var jobRepo = new StubIdentityJobRepository();
        var candidateRepo = new StubRetailCandidateRepository();
        var wikidataCandidateRepo = new StubWikidataCandidateRepository();

        var job = new IdentityJob
        {
            Id = jobId,
            EntityId = entityId,
            EntityType = "MediaAsset",
            MediaType = "Books",
            State = "Queued",
        };
        await jobRepo.CreateAsync(job);

        // Provider returns empty claims
        var provider = new StubExternalMetadataProvider
        {
            Name = "apple_api",
            ProviderId = Guid.NewGuid(),
            Claims = [],
        };

        var configLoader = new StubConfigurationLoader();
        var canonicalRepo = new StubCanonicalValueRepository();
        var claimRepo = new StubMetadataClaimRepository();
        var bridgeIdRepo = new StubBridgeIdRepository();
        var scoringEngine = new StubScoringEngine();
        var retailScoring = new StubRetailMatchScoringService();
        var outcomeFactory = CreateStubStageOutcomeFactory();
        var timeline = CreateStubTimelineRecorder();
        var batchProgress = CreateStubBatchProgressService();

        var retailWorker = new RetailMatchWorker(
            jobRepo,
            candidateRepo,
            outcomeFactory,
            timeline,
            batchProgress,
            new[] { provider },
            retailScoring,
            claimRepo,
            canonicalRepo,
            scoringEngine,
            configLoader,
            bridgeIdRepo,
            new StubWorkRepository(),
            new WorkClaimRouter(),
            new StubHttpClientFactory(),
            null!, // PostPipelineService — not exercised in this test
            NullLogger<RetailMatchWorker>.Instance);

        await retailWorker.PollAsync(CancellationToken.None);

        var updatedJob = await jobRepo.GetByIdAsync(jobId);
        Assert.NotNull(updatedJob);
        Assert.Equal(IdentityJobState.RetailNoMatch.ToString(), updatedJob!.State);

        // Now try WikidataBridgeWorker — it should find 0 jobs because it only
        // leases RetailMatched/RetailMatchedNeedsReview, never RetailNoMatch.
        var bridgeIdHelper = new BridgeIdHelper(configLoader);
        var bridgeWorker = new WikidataBridgeWorker(
            jobRepo,
            wikidataCandidateRepo,
            outcomeFactory,
            timeline,
            bridgeIdHelper,
            new[] { provider },
            bridgeIdRepo,
            claimRepo,
            canonicalRepo,
            scoringEngine,
            configLoader,
            new StubWorkRepository(),
            new WorkClaimRouter(),
            new CatalogUpsertService(new StubWorkRepository()),
            new StubIngestionBatchRepository(),
            NullLogger<WikidataBridgeWorker>.Instance);

        var bridgeProcessed = await bridgeWorker.PollAsync(CancellationToken.None);
        Assert.Equal(0, bridgeProcessed);
    }

    // ── Test 3: WikidataBridgeWorker with no ReconciliationAdapter → QidNoMatch ──

    [Fact]
    public async Task WikidataBridgeWorker_NoReconAdapter_TransitionsToQidNoMatch()
    {
        var entityId = Guid.NewGuid();
        var jobId = Guid.NewGuid();

        var jobRepo = new StubIdentityJobRepository();
        var wikidataCandidateRepo = new StubWikidataCandidateRepository();

        // Seed job directly in RetailMatched state
        var job = new IdentityJob
        {
            Id = jobId,
            EntityId = entityId,
            EntityType = "MediaAsset",
            MediaType = "Books",
            State = IdentityJobState.RetailMatched.ToString(),
        };
        await jobRepo.CreateAsync(job);

        var configLoader = new StubConfigurationLoader();
        var canonicalRepo = new StubCanonicalValueRepository();
        var claimRepo = new StubMetadataClaimRepository();
        var bridgeIdRepo = new StubBridgeIdRepository();
        var scoringEngine = new StubScoringEngine();
        var outcomeFactory = CreateStubStageOutcomeFactory();
        var timeline = CreateStubTimelineRecorder();
        var bridgeIdHelper = new BridgeIdHelper(configLoader);

        // No ReconciliationAdapter in the provider list — only a plain stub
        var plainProvider = new StubExternalMetadataProvider
        {
            Name = "stub_provider",
            ProviderId = Guid.NewGuid(),
            Claims = [],
        };

        var worker = new WikidataBridgeWorker(
            jobRepo,
            wikidataCandidateRepo,
            outcomeFactory,
            timeline,
            bridgeIdHelper,
            new IExternalMetadataProvider[] { plainProvider },
            bridgeIdRepo,
            claimRepo,
            canonicalRepo,
            scoringEngine,
            configLoader,
            new StubWorkRepository(),
            new WorkClaimRouter(),
            new CatalogUpsertService(new StubWorkRepository()),
            new StubIngestionBatchRepository(),
            NullLogger<WikidataBridgeWorker>.Instance);

        var processed = await worker.PollAsync(CancellationToken.None);

        Assert.Equal(1, processed);

        var updatedJob = await jobRepo.GetByIdAsync(jobId);
        Assert.NotNull(updatedJob);
        Assert.Equal(IdentityJobState.QidNoMatch.ToString(), updatedJob!.State);
        Assert.Contains("No reconciliation adapter", updatedJob.LastError);
    }

    // ── Test 4: QuickHydrationWorker completes job ──

    [Fact]
    public async Task QuickHydrationWorker_CompletesJob()
    {
        var entityId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var qid = "Q190159";

        var jobRepo = new StubIdentityJobRepository();
        var enrichment = new StubEnrichmentService();

        var job = new IdentityJob
        {
            Id = jobId,
            EntityId = entityId,
            EntityType = "MediaAsset",
            MediaType = "Books",
            State = IdentityJobState.QidResolved.ToString(),
            ResolvedQid = qid,
        };
        await jobRepo.CreateAsync(job);

        var configLoader = new StubConfigurationLoader();
        var claimRepo = new StubMetadataClaimRepository();
        var canonicalRepo = new StubCanonicalValueRepository();
        var scoringEngine = new StubScoringEngine();
        var reviewRepo = new StubReviewQueueRepository();
        var organizer = new StubAutoOrganizeService();
        var batchProgress = CreateStubBatchProgressService();

        var postPipeline = new PostPipelineService(
            claimRepo,
            canonicalRepo,
            scoringEngine,
            configLoader,
            Array.Empty<IExternalMetadataProvider>(),
            reviewRepo,
            organizer,
            batchProgress,
            NullLogger<PostPipelineService>.Instance);

        var hubAssignment = new HubAssignmentService(
            new NoOpHubRepository(),
            canonicalRepo,
            NullLogger<HubAssignmentService>.Instance);

        var worker = new QuickHydrationWorker(
            jobRepo,
            enrichment,
            hubAssignment,
            postPipeline,
            canonicalRepo,
            configLoader,
            NullLogger<QuickHydrationWorker>.Instance);

        var processed = await worker.PollAsync(CancellationToken.None);

        Assert.Equal(1, processed);

        var updatedJob = await jobRepo.GetByIdAsync(jobId);
        Assert.NotNull(updatedJob);
        Assert.Equal(IdentityJobState.Completed.ToString(), updatedJob!.State);

        Assert.Single(enrichment.Calls);
        Assert.Equal(entityId, enrichment.Calls[0].EntityId);
        Assert.Equal(qid, enrichment.Calls[0].Qid);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Factory helpers for concrete helpers that require repo dependencies
    // ══════════════════════════════════════════════════════════════════════

    private static StageOutcomeFactory CreateStubStageOutcomeFactory()
    {
        return new StageOutcomeFactory(
            new StubReviewQueueRepository(),
            new StubSystemActivityRepository(),
            new StubEventPublisher(),
            new StubCanonicalValueRepository(),
            NullLogger<StageOutcomeFactory>.Instance);
    }

    private static TimelineRecorder CreateStubTimelineRecorder()
    {
        return new TimelineRecorder(new StubEntityTimelineRepository());
    }

    private static BatchProgressService CreateStubBatchProgressService()
    {
        return new BatchProgressService(
            new StubIngestionBatchRepository(),
            new StubEventPublisher(),
            NullLogger<BatchProgressService>.Instance);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Stub implementations
    // ══════════════════════════════════════════════════════════════════════

    // ── StubIdentityJobRepository ────────────────────────────────────────

    private sealed class StubIdentityJobRepository : IIdentityJobRepository
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
            IReadOnlyList<string>? excludeRunIds = null,
            CancellationToken ct = default)
        {
            var stateStrings = states.Select(s => s.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var excluded = excludeRunIds is { Count: > 0 }
                ? new HashSet<string>(excludeRunIds, StringComparer.OrdinalIgnoreCase)
                : null;
            var matches = _jobs
                .Where(j => stateStrings.Contains(j.State)
                         && (excluded is null || j.IngestionRunId is null || !excluded.Contains(j.IngestionRunId.ToString()!)))
                .Take(batchSize)
                .ToList();

            foreach (var j in matches)
            {
                j.LeaseOwner = workerName;
                j.LeaseExpiresAt = DateTimeOffset.UtcNow.Add(leaseDuration);
            }

            return Task.FromResult<IReadOnlyList<IdentityJob>>(matches);
        }

        public Task UpdateStateAsync(Guid jobId, IdentityJobState newState, string? error = null, CancellationToken ct = default)
        {
            var job = _jobs.FirstOrDefault(j => j.Id == jobId);
            if (job is not null)
            {
                job.State = newState.ToString();
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

        public Task<IReadOnlyDictionary<string, int>> GetPendingStage1CountsByRunAsync(
            IReadOnlyList<string> ingestionRunIds, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, int>>(new Dictionary<string, int>());

        public Task<int> ReclaimStuckJobsAsync(TimeSpan stuckThreshold, CancellationToken ct = default)
            => Task.FromResult(0);

        public Task ReleasLeaseAsync(Guid jobId, CancellationToken ct = default)
        {
            var job = _jobs.FirstOrDefault(j => j.Id == jobId);
            if (job is not null)
            {
                job.LeaseOwner = null;
                job.LeaseExpiresAt = null;
            }
            return Task.CompletedTask;
        }

        public Task<int> CountActiveAsync(CancellationToken ct = default)
            => Task.FromResult(_jobs.Count(j =>
                j.State != IdentityJobState.Completed.ToString() &&
                j.State != IdentityJobState.Failed.ToString()));
    }

    // ── StubRetailCandidateRepository ────────────────────────────────────

    private sealed class StubRetailCandidateRepository : IRetailCandidateRepository
    {
        public List<RetailMatchCandidate> Candidates { get; } = [];

        public Task InsertBatchAsync(IReadOnlyList<RetailMatchCandidate> candidates, CancellationToken ct = default)
        {
            Candidates.AddRange(candidates);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<RetailMatchCandidate>> GetByJobAsync(Guid jobId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RetailMatchCandidate>>(
                Candidates.Where(c => c.JobId == jobId).ToList());

        public Task<RetailMatchCandidate?> GetSelectedAsync(Guid jobId, CancellationToken ct = default)
            => Task.FromResult(Candidates.FirstOrDefault(c => c.JobId == jobId && c.Outcome == "AutoAccepted"));

        public Task<RetailMatchCandidate?> GetByIdAsync(Guid candidateId, CancellationToken ct = default)
            => Task.FromResult(Candidates.FirstOrDefault(c => c.Id == candidateId));
    }

    // ── StubWikidataCandidateRepository ──────────────────────────────────

    private sealed class StubWikidataCandidateRepository : IWikidataCandidateRepository
    {
        public List<WikidataBridgeCandidate> Candidates { get; } = [];

        public Task InsertBatchAsync(IReadOnlyList<WikidataBridgeCandidate> candidates, CancellationToken ct = default)
        {
            Candidates.AddRange(candidates);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<WikidataBridgeCandidate>> GetByJobAsync(Guid jobId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<WikidataBridgeCandidate>>(
                Candidates.Where(c => c.JobId == jobId).ToList());

        public Task<WikidataBridgeCandidate?> GetSelectedAsync(Guid jobId, CancellationToken ct = default)
            => Task.FromResult(Candidates.FirstOrDefault(c => c.JobId == jobId && c.Outcome == "AutoAccepted"));

        public Task<WikidataBridgeCandidate?> GetByIdAsync(Guid candidateId, CancellationToken ct = default)
            => Task.FromResult(Candidates.FirstOrDefault(c => c.Id == candidateId));
    }

    // ── StubExternalMetadataProvider ─────────────────────────────────────

    private sealed class StubExternalMetadataProvider : IExternalMetadataProvider
    {
        public string Name { get; set; } = "stub_provider";
        public ProviderDomain Domain => ProviderDomain.Universal;
        public IReadOnlyList<string> CapabilityTags => [];
        public Guid ProviderId { get; set; } = Guid.NewGuid();
        public IReadOnlyList<ProviderClaim> Claims { get; set; } = [];

        public bool CanHandle(MediaType mediaType) => true;
        public bool CanHandle(EntityType entityType) => true;

        public Task<IReadOnlyList<ProviderClaim>> FetchAsync(ProviderLookupRequest request, CancellationToken ct = default)
            => Task.FromResult(Claims);
    }

    // ── StubRetailMatchScoringService ────────────────────────────────────

    private sealed class StubRetailMatchScoringService : IRetailMatchScoringService
    {
        public FieldMatchScores Result { get; set; } = new()
        {
            CompositeScore = 0.0,
        };

        public FieldMatchScores ScoreCandidate(
            IReadOnlyDictionary<string, string> fileHints,
            string? candidateTitle,
            string? candidateAuthor,
            string? candidateYear,
            MediaType mediaType,
            MatchTierConfig? matchTiers = null,
            CandidateExtendedMetadata? extendedMetadata = null,
            double structuralBonus = 0.0)
            => Result;
    }

    // ── StubConfigurationLoader ─────────────────────────────────────────

    private sealed class StubConfigurationLoader : IConfigurationLoader
    {
        public PipelineConfiguration LoadPipelines() => new()
        {
            Pipelines = new Dictionary<string, MediaTypePipeline>(StringComparer.OrdinalIgnoreCase)
            {
                ["Books"] = new()
                {
                    Strategy = ProviderStrategy.Waterfall,
                    Providers = [new PipelineProviderEntry { Rank = 1, Name = "apple_api" }],
                },
            },
        };

        public HydrationSettings LoadHydration() => new();
        public IReadOnlyList<ProviderConfiguration> LoadAllProviders() => [];
        public ScoringSettings LoadScoring() => new();
        public T? LoadConfig<T>(string subdirectory, string name) where T : class => default;

        // Remaining methods throw — not called during tests
        public CoreConfiguration LoadCore() => new();
        public void SaveCore(CoreConfiguration config) => throw new NotImplementedException();
        public void SaveScoring(ScoringSettings settings) => throw new NotImplementedException();
        public MaintenanceSettings LoadMaintenance() => throw new NotImplementedException();
        public void SaveMaintenance(MaintenanceSettings settings) => throw new NotImplementedException();
        public void SaveHydration(HydrationSettings settings) => throw new NotImplementedException();
        public void SavePipelines(PipelineConfiguration config) => throw new NotImplementedException();
        public DisambiguationSettings LoadDisambiguation() => throw new NotImplementedException();
        public void SaveDisambiguation(DisambiguationSettings settings) => throw new NotImplementedException();
        public TranscodingSettings LoadTranscoding() => throw new NotImplementedException();
        public void SaveTranscoding(TranscodingSettings settings) => throw new NotImplementedException();
        public MediaTypeConfiguration LoadMediaTypes() => throw new NotImplementedException();
        public void SaveMediaTypes(MediaTypeConfiguration config) => throw new NotImplementedException();
        public LibrariesConfiguration LoadLibraries() => throw new NotImplementedException();
        public FieldPriorityConfiguration LoadFieldPriorities() => throw new NotImplementedException();
        public void SaveFieldPriorities(FieldPriorityConfiguration config) => throw new NotImplementedException();
        public ProviderConfiguration? LoadProvider(string name) => throw new NotImplementedException();
        public void SaveProvider(ProviderConfiguration config) => throw new NotImplementedException();
        public T? LoadAi<T>() where T : class => throw new NotImplementedException();
        public void SaveAi<T>(T settings) where T : class => throw new NotImplementedException();
        public PaletteConfiguration LoadPalette() => throw new NotImplementedException();
        public void SavePalette(PaletteConfiguration palette) => throw new NotImplementedException();
        public void SaveConfig<T>(string subdirectory, string name, T config) where T : class => throw new NotImplementedException();
    }

    // ── StubCanonicalValueRepository ────────────────────────────────────

    private sealed class StubCanonicalValueRepository : ICanonicalValueRepository
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

    // ── StubMetadataClaimRepository ─────────────────────────────────────

    private sealed class StubMetadataClaimRepository : IMetadataClaimRepository
    {
        public List<MetadataClaim> Claims { get; } = [];

        public Task InsertBatchAsync(IReadOnlyList<MetadataClaim> claims, CancellationToken ct = default)
        {
            Claims.AddRange(claims);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<MetadataClaim>> GetByEntityAsync(Guid entityId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<MetadataClaim>>(Claims.Where(c => c.EntityId == entityId).ToList());

        public Task DeleteByEntityAsync(Guid entityId, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    // ── StubBridgeIdRepository ──────────────────────────────────────────

    private sealed class StubBridgeIdRepository : IBridgeIdRepository
    {
        public List<BridgeIdEntry> Entries { get; set; } = [];

        public Task<IReadOnlyList<BridgeIdEntry>> GetByEntityAsync(Guid entityId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<BridgeIdEntry>>(Entries.Where(e => e.EntityId == entityId).ToList());

        public Task<IReadOnlyDictionary<Guid, IReadOnlyList<BridgeIdEntry>>> GetByEntitiesAsync(IReadOnlyList<Guid> entityIds, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<Guid, IReadOnlyList<BridgeIdEntry>>>(
                entityIds.ToDictionary(
                    id => id,
                    id => (IReadOnlyList<BridgeIdEntry>)Entries.Where(e => e.EntityId == id).ToList()));

        public Task UpsertBatchAsync(IReadOnlyList<BridgeIdEntry> entries, CancellationToken ct = default)
        {
            Entries.AddRange(entries);
            return Task.CompletedTask;
        }

        public Task<BridgeIdEntry?> FindAsync(Guid entityId, string idType, CancellationToken ct = default)
            => Task.FromResult(Entries.FirstOrDefault(e => e.EntityId == entityId && e.IdType == idType));

        public Task<IReadOnlyList<BridgeIdEntry>> FindByValueAsync(string idType, string idValue, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<BridgeIdEntry>>(
                Entries.Where(e => e.IdType == idType && e.IdValue == idValue).ToList());

        public Task UpsertAsync(BridgeIdEntry entry, CancellationToken ct = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }

        public Task DeleteByEntityAsync(Guid entityId, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    // ── StubScoringEngine ───────────────────────────────────────────────

    private sealed class StubScoringEngine : IScoringEngine
    {
        public double OverallConfidence { get; set; } = 0.90;

        public Task<ScoringResult> ScoreEntityAsync(ScoringContext context, CancellationToken ct = default)
            => Task.FromResult(new ScoringResult
            {
                EntityId = context.EntityId,
                OverallConfidence = OverallConfidence,
                ScoredAt = DateTimeOffset.UtcNow,
                FieldScores = [],
            });

        public Task<IReadOnlyList<ScoringResult>> ScoreBatchAsync(IEnumerable<ScoringContext> contexts, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ScoringResult>>(
                contexts.Select(c => ScoreEntityAsync(c, ct).Result).ToList());
    }

    // ── StubEnrichmentService ───────────────────────────────────────────

    private sealed class StubEnrichmentService : IEnrichmentService
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

    // ── StubReviewQueueRepository ───────────────────────────────────────

    private sealed class StubReviewQueueRepository : IReviewQueueRepository
    {
        public Task<Guid> InsertAsync(ReviewQueueEntry entry, CancellationToken ct = default)
            => Task.FromResult(entry.Id);

        public Task<IReadOnlyList<ReviewQueueEntry>> GetPendingAsync(int limit = 50, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ReviewQueueEntry>>([]);

        public Task<ReviewQueueEntry?> GetByIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult<ReviewQueueEntry?>(null);

        public Task<IReadOnlyList<ReviewQueueEntry>> GetByEntityAsync(Guid entityId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ReviewQueueEntry>>([]);

        public Task<IReadOnlyList<ReviewQueueEntry>> GetPendingByEntityAsync(Guid entityId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ReviewQueueEntry>>([]);

        public Task UpdateStatusAsync(Guid id, string status, string? resolvedBy = null, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<int> GetPendingCountAsync(CancellationToken ct = default)
            => Task.FromResult(0);

        public Task<int> DismissAllByEntityAsync(Guid entityId, CancellationToken ct = default)
            => Task.FromResult(0);

        public Task<int> ResolveAllByEntityAsync(Guid entityId, string resolvedBy = "system:auto-organize", CancellationToken ct = default)
            => Task.FromResult(0);

        public Task<int> PurgeOrphanedAsync(CancellationToken ct = default)
            => Task.FromResult(0);
    }

    // ── StubSystemActivityRepository ────────────────────────────────────

    private sealed class StubSystemActivityRepository : ISystemActivityRepository
    {
        public Task LogAsync(SystemActivityEntry entry, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<SystemActivityEntry>> GetRecentAsync(int limit = 50, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SystemActivityEntry>>([]);

        public Task<int> PruneOlderThanAsync(int retentionDays, CancellationToken ct = default)
            => Task.FromResult(0);

        public Task<long> CountAsync(CancellationToken ct = default)
            => Task.FromResult(0L);

        public Task<IReadOnlyList<SystemActivityEntry>> GetByRunIdAsync(Guid runId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SystemActivityEntry>>([]);

        public Task<IReadOnlyList<SystemActivityEntry>> GetRecentByTypesAsync(IReadOnlyList<string> actionTypes, int limit = 50, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SystemActivityEntry>>([]);
    }

    // ── StubEventPublisher ──────────────────────────────────────────────

    private sealed class StubEventPublisher : IEventPublisher
    {
        public Task PublishAsync<TPayload>(string eventName, TPayload payload, CancellationToken ct = default)
            where TPayload : notnull
            => Task.CompletedTask;
    }

    // ── StubEntityTimelineRepository ────────────────────────────────────

    private sealed class StubEntityTimelineRepository : IEntityTimelineRepository
    {
        public Task InsertEventAsync(EntityEvent evt, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task InsertEventsAsync(IReadOnlyList<EntityEvent> events, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<EntityEvent>> GetEventsByEntityAsync(Guid entityId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<EntityEvent>>([]);

        public Task<EntityEvent?> GetLatestEventAsync(Guid entityId, int stage, CancellationToken ct = default)
            => Task.FromResult<EntityEvent?>(null);

        public Task<IReadOnlyList<EntityEvent>> GetCurrentPipelineStateAsync(Guid entityId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<EntityEvent>>([]);

        public Task<EntityEvent?> GetEventByIdAsync(Guid eventId, CancellationToken ct = default)
            => Task.FromResult<EntityEvent?>(null);

        public Task InsertFieldChangesAsync(IReadOnlyList<EntityFieldChange> changes, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<EntityFieldChange>> GetFieldChangesByEventAsync(Guid eventId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<EntityFieldChange>>([]);

        public Task<IReadOnlyList<EntityFieldChange>> GetFieldHistoryAsync(Guid entityId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<EntityFieldChange>>([]);

        public Task<IReadOnlyList<EntityFieldChange>> GetFieldHistoryAsync(Guid entityId, string field, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<EntityFieldChange>>([]);

        public Task<IReadOnlyList<EntityFieldChange>> GetFileOriginalsForEventAsync(Guid eventId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<EntityFieldChange>>([]);

        public Task<IReadOnlyDictionary<Guid, EntityEvent>> GetLatestStage2EventsAsync(IReadOnlyList<Guid> entityIds, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<Guid, EntityEvent>>(new Dictionary<Guid, EntityEvent>());

        public Task<int> CullOldEventsAsync(TimeSpan retention, CancellationToken ct = default)
            => Task.FromResult(0);

        public Task DeleteByEntityAsync(Guid entityId, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    // ── StubIngestionBatchRepository ────────────────────────────────────

    private sealed class StubIngestionBatchRepository : IIngestionBatchRepository
    {
        public Task CreateAsync(IngestionBatch batch, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task UpdateCountsAsync(Guid id, int filesTotal, int filesProcessed, int filesIdentified, int filesReview, int filesNoMatch, int filesFailed, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task CompleteAsync(Guid id, string status, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task IncrementCounterAsync(Guid id, BatchCounterColumn column, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IngestionBatch?> GetByIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult<IngestionBatch?>(null);

        public Task<IReadOnlyList<IngestionBatch>> GetRecentAsync(int limit = 20, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<IngestionBatch>>([]);

        public Task<int> GetNeedsAttentionCountAsync(CancellationToken ct = default)
            => Task.FromResult(0);

        public Task<int> AbandonRunningAsync(CancellationToken ct = default)
            => Task.FromResult(0);
    }

    // ── StubAutoOrganizeService ─────────────────────────────────────────

    private sealed class StubAutoOrganizeService : IAutoOrganizeService
    {
        public Task TryAutoOrganizeAsync(Guid assetId, CancellationToken ct = default, Guid? ingestionRunId = null)
            => Task.CompletedTask;
    }

    // ── NoOpHubRepository ─────────────────────────────────────────────────

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
    private sealed class StubHttpClientFactory : System.Net.Http.IHttpClientFactory
    {
        public System.Net.Http.HttpClient CreateClient(string name)
            => throw new NotSupportedException(
                $"StubHttpClientFactory.CreateClient('{name}') should not be called in unit tests.");
    }

    /// <summary>
    /// Stub IWorkRepository — every method returns a no-op default. The
    /// pipeline tests don't exercise the asset → work lineage path, so
    /// returning null from <see cref="GetLineageByAssetAsync"/> short-circuits
    /// the Phase 3b routing helper without affecting test outcomes.
    /// </summary>
    private sealed class StubWorkRepository : IWorkRepository
    {
        public Task<Guid?> FindParentByKeyAsync(MediaType mediaType, string parentKey, CancellationToken ct = default)
            => Task.FromResult<Guid?>(null);

        public Task<Guid?> FindChildByOrdinalAsync(Guid parentWorkId, int ordinal, CancellationToken ct = default)
            => Task.FromResult<Guid?>(null);

        public Task<Guid?> FindChildByTitleAsync(Guid parentWorkId, string title, CancellationToken ct = default)
            => Task.FromResult<Guid?>(null);

        public Task<Guid?> FindByExternalIdentifierAsync(string scheme, string value, CancellationToken ct = default)
            => Task.FromResult<Guid?>(null);

        public Task<Guid> InsertParentAsync(MediaType mediaType, string parentKey, Guid? grandparentWorkId, int? ordinal, CancellationToken ct = default)
            => Task.FromResult(Guid.NewGuid());

        public Task<Guid> InsertChildAsync(MediaType mediaType, Guid parentWorkId, int? ordinal, CancellationToken ct = default)
            => Task.FromResult(Guid.NewGuid());

        public Task<Guid> InsertStandaloneAsync(MediaType mediaType, CancellationToken ct = default)
            => Task.FromResult(Guid.NewGuid());

        public Task<Guid> InsertCatalogChildAsync(MediaType mediaType, Guid parentWorkId, int? ordinal, IReadOnlyDictionary<string, string>? externalIdentifiers, CancellationToken ct = default)
            => Task.FromResult(Guid.NewGuid());

        public Task PromoteCatalogToOwnedAsync(Guid workId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task WriteExternalIdentifiersAsync(Guid workId, IReadOnlyDictionary<string, string> identifiers, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<WorkLineage?> GetLineageByAssetAsync(Guid assetId, CancellationToken ct = default)
            => Task.FromResult<WorkLineage?>(null);
    }
}
