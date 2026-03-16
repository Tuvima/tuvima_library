using MediaEngine.Domain.Entities;
using MediaEngine.Intelligence.Models;

namespace MediaEngine.Intelligence.Tests;

/// <summary>
/// Additional edge-case tests for <see cref="PriorityCascadeEngine"/>.
/// The ConflictResolver has been replaced by the Priority Cascade — these tests
/// cover cascade-specific behaviour that was not included in ScoringEngineTests.
/// </summary>
public sealed class ConflictResolverTests
{
    private static readonly Guid WikidataProviderId = Guid.Parse("b3000003-d000-4000-8000-000000000004");
    private static readonly Guid ProviderA = Guid.Parse("aaaa0000-0000-0000-0000-000000000001");
    private static readonly Guid ProviderB = Guid.Parse("bbbb0000-0000-0000-0000-000000000002");
    private static readonly Guid EntityId  = Guid.Parse("eeee0000-0000-0000-0000-000000000001");

    private static readonly ScoringConfiguration DefaultConfig = new();

    private static PriorityCascadeEngine CreateEngine() => new();

    // ── Wikidata beats higher-confidence retail claim ─────────────────────────

    [Fact]
    public async Task WikidataClaim_BeatsHigherConfidenceRetailClaim()
    {
        var engine = CreateEngine();
        var context = new ScoringContext
        {
            EntityId = EntityId,
            Claims =
            [
                MakeClaim("title", "Retail Title", ProviderA, 1.0),
                MakeClaim("title", "Wikidata Title", WikidataProviderId, 0.7),
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
        Assert.Equal("Wikidata Title", titleScore.WinningValue);
        Assert.Equal(WikidataProviderId, titleScore.WinningProviderId);
        Assert.False(titleScore.IsConflicted);
    }

    // ── Most recent Wikidata claim wins when multiple Wikidata claims exist ───

    [Fact]
    public async Task MultipleWikidataClaims_MostRecentWins()
    {
        var engine = CreateEngine();

        var older = MakeClaim("title", "Old Wikidata Title", WikidataProviderId, 0.9);
        older.ClaimedAt = DateTimeOffset.UtcNow.AddDays(-10);

        var newer = MakeClaim("title", "New Wikidata Title", WikidataProviderId, 0.8);
        newer.ClaimedAt = DateTimeOffset.UtcNow;

        var context = new ScoringContext
        {
            EntityId = EntityId,
            Claims = [older, newer],
            ProviderWeights = new Dictionary<Guid, double> { [WikidataProviderId] = 1.0 },
            Configuration = DefaultConfig,
        };

        var result = await engine.ScoreEntityAsync(context);

        var titleScore = result.FieldScores.First(f => f.Key == "title");
        Assert.Equal("New Wikidata Title", titleScore.WinningValue);
    }

    // ── Tie-breaking on confidence uses most recent ClaimedAt ─────────────────

    [Fact]
    public async Task TieOnConfidence_MostRecentClaimWins()
    {
        var engine = CreateEngine();

        var older = MakeClaim("title", "Older Claim", ProviderA, 0.8);
        older.ClaimedAt = DateTimeOffset.UtcNow.AddHours(-5);

        var newer = MakeClaim("title", "Newer Claim", ProviderB, 0.8);
        newer.ClaimedAt = DateTimeOffset.UtcNow;

        var context = new ScoringContext
        {
            EntityId = EntityId,
            Claims = [older, newer],
            ProviderWeights = new Dictionary<Guid, double>
            {
                [ProviderA] = 1.0,
                [ProviderB] = 1.0,
            },
            Configuration = DefaultConfig,
        };

        var result = await engine.ScoreEntityAsync(context);

        var titleScore = result.FieldScores.First(f => f.Key == "title");
        Assert.Equal("Newer Claim", titleScore.WinningValue);
    }

    // ── Fields without Wikidata use best retail confidence ────────────────────

    [Fact]
    public async Task RetailOnlyField_UsesBestConfidence()
    {
        var engine = CreateEngine();
        var context = new ScoringContext
        {
            EntityId = EntityId,
            Claims =
            [
                MakeClaim("year", "1965", ProviderA, 0.85),
                MakeClaim("year", "1966", ProviderB, 0.60),
            ],
            ProviderWeights = new Dictionary<Guid, double>
            {
                [ProviderA] = 1.0,
                [ProviderB] = 1.0,
            },
            Configuration = DefaultConfig,
        };

        var result = await engine.ScoreEntityAsync(context);

        var yearScore = result.FieldScores.First(f => f.Key == "year");
        Assert.Equal("1965", yearScore.WinningValue);
        Assert.Equal(0.85, yearScore.Confidence);
    }

    // ── Cancellation is honoured ──────────────────────────────────────────────

    [Fact]
    public async Task CancelledToken_ThrowsOperationCancelledException()
    {
        var engine = CreateEngine();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var context = new ScoringContext
        {
            EntityId = EntityId,
            Claims = [MakeClaim("title", "Dune", ProviderA, 0.9)],
            ProviderWeights = new Dictionary<Guid, double> { [ProviderA] = 1.0 },
            Configuration = DefaultConfig,
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => engine.ScoreEntityAsync(context, cts.Token));
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
