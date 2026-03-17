using MediaEngine.Domain.Entities;
using MediaEngine.Intelligence.Models;
using MediaEngine.Storage.Contracts;
using MediaEngine.Storage.Models;

namespace MediaEngine.Intelligence.Tests;

/// <summary>
/// Tests for <see cref="PriorityCascadeEngine"/> — the Priority Cascade that resolves
/// competing metadata claims into canonical values.
/// </summary>
public sealed class ScoringEngineTests
{
    private static readonly Guid WikidataProviderId = Guid.Parse("b3000003-d000-4000-8000-000000000004");
    private static readonly Guid ProviderA = Guid.Parse("aaaa0000-0000-0000-0000-000000000001");
    private static readonly Guid ProviderB = Guid.Parse("bbbb0000-0000-0000-0000-000000000002");
    private static readonly Guid EntityId  = Guid.Parse("eeee0000-0000-0000-0000-000000000001");

    private static readonly ScoringConfiguration DefaultConfig = new();

    private static PriorityCascadeEngine CreateEngine() => new(new StubConfigurationLoader());

    // ── Single claim wins by default ─────────────────────────────────────────

    [Fact]
    public async Task SingleClaim_WinsByDefault()
    {
        var engine = CreateEngine();
        var context = new ScoringContext
        {
            EntityId = EntityId,
            Claims = [MakeClaim("title", "Dune", ProviderA, 0.9)],
            ProviderWeights = new Dictionary<Guid, double> { [ProviderA] = 1.0 },
            Configuration = DefaultConfig,
        };

        var result = await engine.ScoreEntityAsync(context);

        Assert.Single(result.FieldScores);
        Assert.Equal("Dune", result.FieldScores[0].WinningValue);
        Assert.Equal(0.9, result.FieldScores[0].Confidence);
        Assert.False(result.FieldScores[0].IsConflicted);
    }

    // ── Wikidata claim always wins over other providers ───────────────────────

    [Fact]
    public async Task WikidataClaim_AlwaysWins()
    {
        var engine = CreateEngine();
        var context = new ScoringContext
        {
            EntityId = EntityId,
            Claims =
            [
                MakeClaim("title", "Dune: Retail Title", ProviderA, 1.0),
                MakeClaim("title", "Dune", WikidataProviderId, 0.95),
            ],
            ProviderWeights = new Dictionary<Guid, double>
            {
                [ProviderA]          = 1.0,
                [WikidataProviderId] = 1.0,
            },
            Configuration = DefaultConfig,
        };

        var result = await engine.ScoreEntityAsync(context);

        var titleScore = result.FieldScores.First(f => f.Key == "title");
        Assert.Equal("Dune", titleScore.WinningValue);
        Assert.Equal(WikidataProviderId, titleScore.WinningProviderId);
    }

    // ── Wikidata wins even over user-locked claims ───────────────────────────

    [Fact]
    public async Task WikidataClaim_WinsOverUserLock()
    {
        var engine = CreateEngine();
        var lockedClaim = MakeClaim("title", "My Custom Title", ProviderA, 0.5);
        lockedClaim.IsUserLocked = true;

        var context = new ScoringContext
        {
            EntityId = EntityId,
            Claims =
            [
                lockedClaim,
                MakeClaim("title", "Dune", WikidataProviderId, 0.95),
            ],
            ProviderWeights = new Dictionary<Guid, double>
            {
                [ProviderA]          = 0.1,
                [WikidataProviderId] = 1.0,
            },
            Configuration = DefaultConfig,
        };

        var result = await engine.ScoreEntityAsync(context);

        var titleScore = result.FieldScores.First(f => f.Key == "title");
        Assert.Equal("Dune", titleScore.WinningValue);
        Assert.Equal(WikidataProviderId, titleScore.WinningProviderId);
    }

    // ── User-locked claim wins when no Wikidata value present ────────────────

    [Fact]
    public async Task UserLockedClaim_WinsWhenNoWikidata()
    {
        var engine = CreateEngine();
        var lockedClaim = MakeClaim("title", "My Custom Title", ProviderA, 0.5);
        lockedClaim.IsUserLocked = true;

        var context = new ScoringContext
        {
            EntityId = EntityId,
            Claims =
            [
                MakeClaim("title", "Dune", ProviderB, 1.0),
                lockedClaim,
            ],
            ProviderWeights = new Dictionary<Guid, double>
            {
                [ProviderA] = 0.1,
                [ProviderB] = 1.0,
            },
            Configuration = DefaultConfig,
        };

        var result = await engine.ScoreEntityAsync(context);

        var titleScore = result.FieldScores.First(f => f.Key == "title");
        Assert.Equal("My Custom Title", titleScore.WinningValue);
        Assert.Equal(1.0, titleScore.Confidence);
        Assert.False(titleScore.IsConflicted);
    }

    // ── Multiple user-locked claims: most recent wins ────────────────────────

    [Fact]
    public async Task MultipleUserLockedClaims_MostRecentWins()
    {
        var engine = CreateEngine();

        var older = MakeClaim("title", "Old Title", ProviderA, 1.0);
        older.IsUserLocked = true;
        older.ClaimedAt = DateTimeOffset.UtcNow.AddDays(-10);

        var newer = MakeClaim("title", "New Title", ProviderA, 1.0);
        newer.IsUserLocked = true;
        newer.ClaimedAt = DateTimeOffset.UtcNow;

        var context = new ScoringContext
        {
            EntityId = EntityId,
            Claims = [older, newer],
            ProviderWeights = new Dictionary<Guid, double> { [ProviderA] = 1.0 },
            Configuration = DefaultConfig,
        };

        var result = await engine.ScoreEntityAsync(context);

        var titleScore = result.FieldScores.First(f => f.Key == "title");
        Assert.Equal("New Title", titleScore.WinningValue);
    }

    // ── Highest-confidence non-Wikidata claim wins ───────────────────────────

    [Fact]
    public async Task HighestConfidenceClaim_WinsWhenNoWikidata()
    {
        var engine = CreateEngine();
        var context = new ScoringContext
        {
            EntityId = EntityId,
            Claims =
            [
                MakeClaim("title", "Dune", ProviderA, 0.9),
                MakeClaim("title", "DUNE: Novel", ProviderB, 0.5),
            ],
            ProviderWeights = new Dictionary<Guid, double>
            {
                [ProviderA] = 1.0,
                [ProviderB] = 1.0,
            },
            Configuration = DefaultConfig,
        };

        var result = await engine.ScoreEntityAsync(context);

        var titleScore = result.FieldScores.First(f => f.Key == "title");
        Assert.Equal("Dune", titleScore.WinningValue);
    }

    // ── IsConflicted is always false in cascade model ─────────────────────────

    [Fact]
    public async Task IsConflicted_IsAlwaysFalse()
    {
        var engine = CreateEngine();
        var context = new ScoringContext
        {
            EntityId = EntityId,
            Claims =
            [
                MakeClaim("title", "Dune", ProviderA, 0.9),
                MakeClaim("title", "Dune: A Novel", ProviderB, 0.88),
            ],
            ProviderWeights = new Dictionary<Guid, double>
            {
                [ProviderA] = 1.0,
                [ProviderB] = 1.0,
            },
            Configuration = DefaultConfig,
        };

        var result = await engine.ScoreEntityAsync(context);

        Assert.All(result.FieldScores, f => Assert.False(f.IsConflicted));
    }

    // ── Overall confidence = mean of field confidences ───────────────────────

    [Fact]
    public async Task OverallConfidence_IsMeanOfFieldConfidences()
    {
        var engine = CreateEngine();
        var context = new ScoringContext
        {
            EntityId = EntityId,
            Claims =
            [
                MakeClaim("title", "Dune", ProviderA, 0.9),
                MakeClaim("author", "Frank Herbert", ProviderA, 0.7),
            ],
            ProviderWeights = new Dictionary<Guid, double> { [ProviderA] = 1.0 },
            Configuration = DefaultConfig,
        };

        var result = await engine.ScoreEntityAsync(context);

        Assert.Equal(2, result.FieldScores.Count);
        Assert.Equal(0.8, result.OverallConfidence, precision: 10);
    }

    // ── Empty claims → 0.0 overall confidence ────────────────────────────────

    [Fact]
    public async Task EmptyClaims_ZeroOverallConfidence()
    {
        var engine = CreateEngine();
        var context = new ScoringContext
        {
            EntityId = EntityId,
            Claims = [],
            ProviderWeights = new Dictionary<Guid, double>(),
            Configuration = DefaultConfig,
        };

        var result = await engine.ScoreEntityAsync(context);

        Assert.Empty(result.FieldScores);
        Assert.Equal(0.0, result.OverallConfidence);
    }

    // ── Multiple fields scored independently ─────────────────────────────────

    [Fact]
    public async Task MultipleFields_ScoredIndependently()
    {
        var engine = CreateEngine();
        var context = new ScoringContext
        {
            EntityId = EntityId,
            Claims =
            [
                MakeClaim("title", "Dune", ProviderA, 0.9),
                MakeClaim("title", "DUNE", ProviderB, 0.8),
                MakeClaim("author", "Frank Herbert", ProviderA, 0.95),
                MakeClaim("year", "1965", ProviderA, 0.85),
            ],
            ProviderWeights = new Dictionary<Guid, double>
            {
                [ProviderA] = 0.8,
                [ProviderB] = 0.5,
            },
            Configuration = DefaultConfig,
        };

        var result = await engine.ScoreEntityAsync(context);

        Assert.Equal(3, result.FieldScores.Count);
        Assert.Contains(result.FieldScores, f => f.Key == "title");
        Assert.Contains(result.FieldScores, f => f.Key == "author");
        Assert.Contains(result.FieldScores, f => f.Key == "year");
    }

    // ── Batch scoring ────────────────────────────────────────────────────────

    [Fact]
    public async Task ScoreBatch_ScoresAllContexts()
    {
        var engine = CreateEngine();
        var entity1 = Guid.NewGuid();
        var entity2 = Guid.NewGuid();

        var contexts = new[]
        {
            new ScoringContext
            {
                EntityId = entity1,
                Claims = [MakeClaim("title", "Book 1", ProviderA, 0.9)],
                ProviderWeights = new Dictionary<Guid, double> { [ProviderA] = 1.0 },
                Configuration = DefaultConfig,
            },
            new ScoringContext
            {
                EntityId = entity2,
                Claims = [MakeClaim("title", "Book 2", ProviderA, 0.9)],
                ProviderWeights = new Dictionary<Guid, double> { [ProviderA] = 1.0 },
                Configuration = DefaultConfig,
            },
        };

        var results = await engine.ScoreBatchAsync(contexts);

        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.EntityId == entity1);
        Assert.Contains(results, r => r.EntityId == entity2);
    }

    // ── Helper ───────────────────────────────────────────────────────────────

    private static MetadataClaim MakeClaim(
        string key, string value, Guid providerId, double confidence) => new()
    {
        Id         = Guid.NewGuid(),
        EntityId   = EntityId,
        ProviderId = providerId,
        ClaimKey   = key,
        ClaimValue = value,
        Confidence = confidence,
        ClaimedAt  = DateTimeOffset.UtcNow,
    };
}
