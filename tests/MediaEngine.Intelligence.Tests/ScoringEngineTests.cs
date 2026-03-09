using MediaEngine.Domain.Entities;
using MediaEngine.Intelligence.Models;
using MediaEngine.Intelligence.Strategies;

namespace MediaEngine.Intelligence.Tests;

/// <summary>
/// Tests for <see cref="ScoringEngine"/> — the Weighted Voter that resolves
/// competing metadata claims into canonical values.
/// </summary>
public sealed class ScoringEngineTests
{
    private static readonly Guid ProviderA = Guid.Parse("aaaa0000-0000-0000-0000-000000000001");
    private static readonly Guid ProviderB = Guid.Parse("bbbb0000-0000-0000-0000-000000000002");
    private static readonly Guid EntityId  = Guid.Parse("eeee0000-0000-0000-0000-000000000001");

    private static readonly ScoringConfiguration DefaultConfig = new();

    private static ScoringEngine CreateEngine() =>
        new(new ConflictResolver([new ExactMatchStrategy(), new LevenshteinStrategy()]));

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
        Assert.Equal(1.0, result.FieldScores[0].Confidence);
        Assert.False(result.FieldScores[0].IsConflicted);
    }

    // ── Higher-weighted provider wins ────────────────────────────────────────

    [Fact]
    public async Task HigherWeightedProvider_Wins()
    {
        var engine = CreateEngine();
        var context = new ScoringContext
        {
            EntityId = EntityId,
            Claims =
            [
                MakeClaim("title", "Dune", ProviderA, 0.9),
                MakeClaim("title", "DUNE: Novel", ProviderB, 0.9),
            ],
            ProviderWeights = new Dictionary<Guid, double>
            {
                [ProviderA] = 0.9,
                [ProviderB] = 0.3,
            },
            Configuration = DefaultConfig,
        };

        var result = await engine.ScoreEntityAsync(context);

        var titleScore = result.FieldScores.First(f => f.Key == "title");
        Assert.Equal("Dune", titleScore.WinningValue);
    }

    // ── User-locked claim always wins at confidence 1.0 ──────────────────────

    [Fact]
    public async Task UserLockedClaim_AlwaysWinsAtConfidence1()
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

    // ── Field-specific weights override global weights ───────────────────────

    [Fact]
    public async Task FieldSpecificWeights_OverrideGlobalWeights()
    {
        var engine = CreateEngine();
        var context = new ScoringContext
        {
            EntityId = EntityId,
            Claims =
            [
                MakeClaim("narrator", "Narrator A", ProviderA, 0.9),
                MakeClaim("narrator", "Narrator B", ProviderB, 0.9),
            ],
            ProviderWeights = new Dictionary<Guid, double>
            {
                [ProviderA] = 0.5,
                [ProviderB] = 0.5,
            },
            Configuration = DefaultConfig,
            ProviderFieldWeights = new Dictionary<Guid, IReadOnlyDictionary<string, double>>
            {
                [ProviderA] = new Dictionary<string, double> { ["narrator"] = 0.9 },
                [ProviderB] = new Dictionary<string, double> { ["narrator"] = 0.1 },
            },
        };

        var result = await engine.ScoreEntityAsync(context);

        var narratorScore = result.FieldScores.First(f => f.Key == "narrator");
        Assert.Equal("Narrator A", narratorScore.WinningValue);
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
                MakeClaim("author", "Frank Herbert", ProviderA, 0.9),
            ],
            ProviderWeights = new Dictionary<Guid, double> { [ProviderA] = 1.0 },
            Configuration = DefaultConfig,
        };

        var result = await engine.ScoreEntityAsync(context);

        Assert.Equal(2, result.FieldScores.Count);
        Assert.Equal(1.0, result.OverallConfidence);
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
