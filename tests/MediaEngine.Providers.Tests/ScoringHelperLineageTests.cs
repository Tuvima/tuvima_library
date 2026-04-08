using MediaEngine.Domain;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Intelligence.Contracts;
using MediaEngine.Intelligence.Models;
using MediaEngine.Providers.Contracts;
using MediaEngine.Providers.Models;
using MediaEngine.Providers.Services;
using MediaEngine.Storage.Contracts;
using MediaEngine.Storage.Models;
using ProviderConfiguration = MediaEngine.Storage.Models.ProviderConfiguration;

namespace MediaEngine.Providers.Tests;

/// <summary>
/// Phase 3c — verifies that <see cref="ScoringHelper.PersistAndScoreWithLineageAsync"/>
/// performs lineage-aware dual-write: every claim still lands on the asset id
/// (so existing readers don't regress), and parent-scoped claims additionally
/// land on the parent Work id when the asset has a real parent in the
/// hierarchy.
/// </summary>
public sealed class ScoringHelperLineageTests
{
    // ── Test 1: no lineage → behaves exactly like the old single-entity write ──

    [Fact]
    public async Task NoLineage_PersistsAllClaimsToAssetIdOnly()
    {
        var assetId = Guid.NewGuid();
        var providerId = Guid.NewGuid();
        var claimRepo = new RecordingClaimRepository();
        var canonicalRepo = new RecordingCanonicalRepository();

        var claims = new[]
        {
            new ProviderClaim(MetadataFieldConstants.Title,    "Episode 1",    0.9),
            new ProviderClaim(MetadataFieldConstants.ShowName, "Severance",    0.9),
            new ProviderClaim(MetadataFieldConstants.Year,     "2022",          0.9),
        };

        await ScoringHelper.PersistAndScoreWithLineageAsync(
            assetId, claims, providerId,
            lineage: null,
            claimRepo, canonicalRepo,
            new RecordingScoringEngine(), new MinimalConfigLoader(),
            allProviders: [], CancellationToken.None);

        Assert.Equal(3, claimRepo.Inserted.Count);
        Assert.All(claimRepo.Inserted, c => Assert.Equal(assetId, c.EntityId));
    }

    // ── Test 2: lineage with self == parent (movie) → no parent mirror ──

    [Fact]
    public async Task LineageWithoutRealParent_PersistsToAssetIdOnly()
    {
        var assetId = Guid.NewGuid();
        var workId  = Guid.NewGuid();   // parent collapses to self for movies
        var providerId = Guid.NewGuid();
        var claimRepo = new RecordingClaimRepository();
        var canonicalRepo = new RecordingCanonicalRepository();

        var lineage = new WorkLineage(
            AssetId:          assetId,
            EditionId:        Guid.NewGuid(),
            WorkId:           workId,
            ParentWorkId:     null,
            RootParentWorkId: workId,        // standalone — parent collapses to self
            WorkKind:         WorkKind.Standalone,
            MediaType:        MediaType.Movies);

        var claims = new[]
        {
            new ProviderClaim(MetadataFieldConstants.Title, "Dune",    0.9),
            new ProviderClaim(MetadataFieldConstants.Year,  "2021",     0.9),
            new ProviderClaim(MetadataFieldConstants.Director, "Villeneuve", 0.9),
        };

        await ScoringHelper.PersistAndScoreWithLineageAsync(
            assetId, claims, providerId, lineage,
            claimRepo, canonicalRepo,
            new RecordingScoringEngine(), new MinimalConfigLoader(),
            allProviders: [], CancellationToken.None);

        // Only one persistence pass — all 3 claims keyed by the asset.
        Assert.Equal(3, claimRepo.Inserted.Count);
        Assert.All(claimRepo.Inserted, c => Assert.Equal(assetId, c.EntityId));
    }

    // ── Test 3: TV episode under a show → parent claims mirrored ──

    [Fact]
    public async Task TvEpisode_MirrorsParentScopedClaimsToShowWork()
    {
        var assetId = Guid.NewGuid();
        var episodeWorkId = Guid.NewGuid();
        var showWorkId    = Guid.NewGuid();
        var providerId    = Guid.NewGuid();

        var claimRepo     = new RecordingClaimRepository();
        var canonicalRepo = new RecordingCanonicalRepository();

        var lineage = new WorkLineage(
            AssetId:          assetId,
            EditionId:        Guid.NewGuid(),
            WorkId:           episodeWorkId,
            ParentWorkId:     Guid.NewGuid(),  // season
            RootParentWorkId: showWorkId,       // show
            WorkKind:         WorkKind.Child,
            MediaType:        MediaType.TV);

        var claims = new[]
        {
            // Self-scope (episode level)
            new ProviderClaim(MetadataFieldConstants.Title,         "Hide and Seek", 0.9),
            new ProviderClaim(MetadataFieldConstants.EpisodeNumber, "5",              0.9),
            new ProviderClaim(MetadataFieldConstants.Director,      "Aoife McArdle",  0.9),

            // Parent-scope (show level — mirror onto showWorkId)
            new ProviderClaim(MetadataFieldConstants.ShowName,    "Severance",  0.9),
            new ProviderClaim(MetadataFieldConstants.Year,        "2022",        0.9),
            new ProviderClaim(MetadataFieldConstants.Description, "Workplace …", 0.9),
            new ProviderClaim(MetadataFieldConstants.Genre,       "Sci-Fi",      0.9),
        };

        await ScoringHelper.PersistAndScoreWithLineageAsync(
            assetId, claims, providerId, lineage,
            claimRepo, canonicalRepo,
            new RecordingScoringEngine(), new MinimalConfigLoader(),
            allProviders: [], CancellationToken.None);

        // Asset received the full picture (no reader regression).
        var assetClaims = claimRepo.Inserted.Where(c => c.EntityId == assetId).ToList();
        Assert.Equal(7, assetClaims.Count);

        // Show Work received only the parent-scoped claims.
        var showClaims = claimRepo.Inserted.Where(c => c.EntityId == showWorkId).ToList();
        Assert.Equal(4, showClaims.Count);
        Assert.Contains(showClaims, c => c.ClaimKey == MetadataFieldConstants.ShowName);
        Assert.Contains(showClaims, c => c.ClaimKey == MetadataFieldConstants.Year);
        Assert.Contains(showClaims, c => c.ClaimKey == MetadataFieldConstants.Description);
        Assert.Contains(showClaims, c => c.ClaimKey == MetadataFieldConstants.Genre);

        // Self-scope claims are NOT duplicated onto the show.
        Assert.DoesNotContain(showClaims, c => c.ClaimKey == MetadataFieldConstants.Title);
        Assert.DoesNotContain(showClaims, c => c.ClaimKey == MetadataFieldConstants.EpisodeNumber);
        Assert.DoesNotContain(showClaims, c => c.ClaimKey == MetadataFieldConstants.Director);
    }

    // ── Test 4: music track under an album → year/album/cover go to album Work ──

    [Fact]
    public async Task MusicTrack_MirrorsAlbumScopedClaimsToAlbumWork()
    {
        var assetId = Guid.NewGuid();
        var trackWorkId = Guid.NewGuid();
        var albumWorkId = Guid.NewGuid();
        var providerId  = Guid.NewGuid();

        var claimRepo     = new RecordingClaimRepository();
        var canonicalRepo = new RecordingCanonicalRepository();

        var lineage = new WorkLineage(
            AssetId:          assetId,
            EditionId:        Guid.NewGuid(),
            WorkId:           trackWorkId,
            ParentWorkId:     albumWorkId,
            RootParentWorkId: albumWorkId,
            WorkKind:         WorkKind.Child,
            MediaType:        MediaType.Music);

        var claims = new[]
        {
            // Self
            new ProviderClaim(MetadataFieldConstants.Title,       "Time",           0.9),
            new ProviderClaim(MetadataFieldConstants.TrackNumber, "4",               0.9),

            // Parent (album)
            new ProviderClaim(MetadataFieldConstants.Album,    "The Dark Side of the Moon", 0.9),
            new ProviderClaim(MetadataFieldConstants.Author,   "Pink Floyd",                 0.9),
            new ProviderClaim(MetadataFieldConstants.Year,     "1973",                        0.9),
            new ProviderClaim(MetadataFieldConstants.CoverUrl, "https://…/cover.jpg",        0.9),
        };

        await ScoringHelper.PersistAndScoreWithLineageAsync(
            assetId, claims, providerId, lineage,
            claimRepo, canonicalRepo,
            new RecordingScoringEngine(), new MinimalConfigLoader(),
            allProviders: [], CancellationToken.None);

        var trackClaims = claimRepo.Inserted.Where(c => c.EntityId == assetId).ToList();
        var albumClaims = claimRepo.Inserted.Where(c => c.EntityId == albumWorkId).ToList();

        Assert.Equal(6, trackClaims.Count);

        // Album Work receives album, author, year, cover_url (4 parent-scoped claims).
        Assert.Equal(4, albumClaims.Count);
        Assert.Contains(albumClaims, c => c.ClaimKey == MetadataFieldConstants.Album);
        Assert.Contains(albumClaims, c => c.ClaimKey == MetadataFieldConstants.Author);
        Assert.Contains(albumClaims, c => c.ClaimKey == MetadataFieldConstants.Year);
        Assert.Contains(albumClaims, c => c.ClaimKey == MetadataFieldConstants.CoverUrl);

        // Title and TrackNumber stay on the track Work only.
        Assert.DoesNotContain(albumClaims, c => c.ClaimKey == MetadataFieldConstants.Title);
        Assert.DoesNotContain(albumClaims, c => c.ClaimKey == MetadataFieldConstants.TrackNumber);
    }

    // ── Test 5: empty claim list short-circuits cleanly ──

    [Fact]
    public async Task EmptyClaims_NoOp()
    {
        var assetId = Guid.NewGuid();
        var claimRepo = new RecordingClaimRepository();
        var canonicalRepo = new RecordingCanonicalRepository();

        await ScoringHelper.PersistAndScoreWithLineageAsync(
            assetId, [], Guid.NewGuid(),
            lineage: null,
            claimRepo, canonicalRepo,
            new RecordingScoringEngine(), new MinimalConfigLoader(),
            allProviders: [], CancellationToken.None);

        Assert.Empty(claimRepo.Inserted);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Minimal stubs (self-contained — no dependency on WorkerPipelineTests)
    // ─────────────────────────────────────────────────────────────────────

    private sealed class RecordingClaimRepository : IMetadataClaimRepository
    {
        public List<MetadataClaim> Inserted { get; } = [];

        public Task InsertBatchAsync(IReadOnlyList<MetadataClaim> claims, CancellationToken ct = default)
        {
            Inserted.AddRange(claims);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<MetadataClaim>> GetByEntityAsync(Guid entityId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<MetadataClaim>>(
                Inserted.Where(c => c.EntityId == entityId).ToList());

        public Task DeleteByEntityAsync(Guid entityId, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class RecordingCanonicalRepository : ICanonicalValueRepository
    {
        public List<CanonicalValue> Upserted { get; } = [];

        public Task<IReadOnlyList<CanonicalValue>> GetByEntityAsync(Guid entityId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CanonicalValue>>([]);

        public Task<IReadOnlyDictionary<Guid, IReadOnlyList<CanonicalValue>>> GetByEntitiesAsync(
            IReadOnlyList<Guid> entityIds, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<Guid, IReadOnlyList<CanonicalValue>>>(
                new Dictionary<Guid, IReadOnlyList<CanonicalValue>>());

        public Task UpsertBatchAsync(IReadOnlyList<CanonicalValue> values, CancellationToken ct = default)
        {
            Upserted.AddRange(values);
            return Task.CompletedTask;
        }

        public Task DeleteByEntityAsync(Guid entityId, CancellationToken ct = default) => Task.CompletedTask;

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

    private sealed class RecordingScoringEngine : IScoringEngine
    {
        public Task<ScoringResult> ScoreEntityAsync(ScoringContext context, CancellationToken ct = default)
            => Task.FromResult(new ScoringResult
            {
                EntityId = context.EntityId,
                OverallConfidence = 0.9,
                ScoredAt = DateTimeOffset.UtcNow,
                FieldScores = [],
            });

        public Task<IReadOnlyList<ScoringResult>> ScoreBatchAsync(IEnumerable<ScoringContext> contexts, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ScoringResult>>(
                contexts.Select(c => ScoreEntityAsync(c, ct).Result).ToList());
    }

    private sealed class MinimalConfigLoader : IConfigurationLoader
    {
        public ScoringSettings LoadScoring() => new();
        public IReadOnlyList<ProviderConfiguration> LoadAllProviders() => [];

        public PipelineConfiguration LoadPipelines() => new();
        public HydrationSettings LoadHydration() => new();
        public T? LoadConfig<T>(string subdirectory, string name) where T : class => default;
        public CoreConfiguration LoadCore() => throw new NotImplementedException();
        public void SaveCore(CoreConfiguration config) => throw new NotImplementedException();
        public void SaveScoring(ScoringSettings settings) => throw new NotImplementedException();
        public MaintenanceSettings LoadMaintenance() => throw new NotImplementedException();
        public void SaveMaintenance(MaintenanceSettings settings) => throw new NotImplementedException();
        public void SaveHydration(HydrationSettings settings) => throw new NotImplementedException();
        public ProviderSlotConfiguration LoadSlots() => throw new NotImplementedException();
        public void SaveSlots(ProviderSlotConfiguration slots) => throw new NotImplementedException();
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
}
