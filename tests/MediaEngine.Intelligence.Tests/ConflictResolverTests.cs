using MediaEngine.Domain.Entities;
using MediaEngine.Intelligence.Models;
using MediaEngine.Intelligence.Strategies;

namespace MediaEngine.Intelligence.Tests;

/// <summary>
/// Tests for <see cref="ConflictResolver"/> — selects winning value per field
/// using provider-weighted, time-decayed claim scores.
/// </summary>
public sealed class ConflictResolverTests
{
    private static readonly Guid ProviderA = Guid.Parse("aaaa0000-0000-0000-0000-000000000001");
    private static readonly Guid ProviderB = Guid.Parse("bbbb0000-0000-0000-0000-000000000002");
    private static readonly Guid EntityId  = Guid.Parse("eeee0000-0000-0000-0000-000000000001");

    private static readonly ScoringConfiguration DefaultConfig = new();

    private static ConflictResolver CreateResolver() =>
        new([new ExactMatchStrategy(), new LevenshteinStrategy()]);

    // ── Single claim → no conflict ───────────────────────────────────────────

    [Fact]
    public void SingleClaim_NoConflict()
    {
        var resolver = CreateResolver();
        var claims = new List<MetadataClaim> { MakeClaim("title", "Dune", ProviderA, 0.9) };
        var weights = new Dictionary<Guid, double> { [ProviderA] = 1.0 };

        var resolution = resolver.Resolve("title", claims, weights, DefaultConfig);

        Assert.Equal("Dune", resolution.WinningClaim.ClaimValue);
        Assert.False(resolution.IsConflicted);
        Assert.Equal(1.0, resolution.AdjustedConfidence); // single claim normalises to 1.0
    }

    // ── Two claims, same value → grouped, no conflict ────────────────────────

    [Fact]
    public void TwoClaims_SameValue_NotConflicted()
    {
        var resolver = CreateResolver();
        var claims = new List<MetadataClaim>
        {
            MakeClaim("title", "Dune", ProviderA, 0.9),
            MakeClaim("title", "dune", ProviderB, 0.8),  // same value, different case
        };
        var weights = new Dictionary<Guid, double>
        {
            [ProviderA] = 1.0,
            [ProviderB] = 1.0,
        };

        var resolution = resolver.Resolve("title", claims, weights, DefaultConfig);

        // Both claims grouped under "dune" (normalised) → single group → no conflict.
        Assert.False(resolution.IsConflicted);
        Assert.Equal(1.0, resolution.AdjustedConfidence);
    }

    // ── Two competing values → winner determined by weight ───────────────────

    [Fact]
    public void CompetingValues_HigherWeightWins()
    {
        var resolver = CreateResolver();
        var claims = new List<MetadataClaim>
        {
            MakeClaim("title", "Dune", ProviderA, 0.9),
            MakeClaim("title", "Dune: A Novel", ProviderB, 0.9),
        };
        var weights = new Dictionary<Guid, double>
        {
            [ProviderA] = 0.9,
            [ProviderB] = 0.3,
        };

        var resolution = resolver.Resolve("title", claims, weights, DefaultConfig);

        Assert.Equal("Dune", resolution.WinningClaim.ClaimValue);
    }

    // ── Close values → conflicted ────────────────────────────────────────────

    [Fact]
    public void CloseValues_FlaggedAsConflicted()
    {
        var resolver = CreateResolver();
        // Two providers with nearly equal weight × confidence.
        var claims = new List<MetadataClaim>
        {
            MakeClaim("title", "Dune", ProviderA, 0.9),
            MakeClaim("title", "Dune: A Novel", ProviderB, 0.88),
        };
        var weights = new Dictionary<Guid, double>
        {
            [ProviderA] = 1.0,
            [ProviderB] = 1.0,
        };

        var resolution = resolver.Resolve("title", claims, weights, DefaultConfig);

        Assert.True(resolution.IsConflicted);
    }

    // ── Stale claim decay ────────────────────────────────────────────────────

    [Fact]
    public void StaleClaim_GetsDecayedWeight()
    {
        var resolver = CreateResolver();
        var config = new ScoringConfiguration
        {
            StaleClaimDecayDays = 30,
            StaleClaimDecayFactor = 0.5,
        };

        var freshClaim = MakeClaim("title", "Fresh Value", ProviderA, 0.8);
        freshClaim.ClaimedAt = DateTimeOffset.UtcNow;

        var staleClaim = MakeClaim("title", "Stale Value", ProviderB, 0.9);
        staleClaim.ClaimedAt = DateTimeOffset.UtcNow.AddDays(-60); // older than 30 days

        var claims = new List<MetadataClaim> { freshClaim, staleClaim };
        var weights = new Dictionary<Guid, double>
        {
            [ProviderA] = 1.0,
            [ProviderB] = 1.0,
        };

        var resolution = resolver.Resolve("title", claims, weights, config);

        // Fresh claim should win because stale claim gets 0.5 decay factor.
        Assert.Equal("Fresh Value", resolution.WinningClaim.ClaimValue);
    }

    // ── Zero decay days disables staleness ───────────────────────────────────

    [Fact]
    public void ZeroDecayDays_DisablesDecay()
    {
        var resolver = CreateResolver();
        var config = new ScoringConfiguration
        {
            StaleClaimDecayDays = 0, // disabled
        };

        var oldClaim = MakeClaim("title", "Old Value", ProviderA, 0.9);
        oldClaim.ClaimedAt = DateTimeOffset.UtcNow.AddDays(-365);

        var claims = new List<MetadataClaim> { oldClaim };
        var weights = new Dictionary<Guid, double> { [ProviderA] = 1.0 };

        var resolution = resolver.Resolve("title", claims, weights, config);

        // No decay applied; old claim still wins.
        Assert.Equal("Old Value", resolution.WinningClaim.ClaimValue);
        Assert.Equal(1.0, resolution.AdjustedConfidence);
    }

    // ── Unknown provider defaults to weight 1.0 ──────────────────────────────

    [Fact]
    public void UnknownProvider_DefaultsToWeight1()
    {
        var resolver = CreateResolver();
        var unknownProvider = Guid.NewGuid();
        var claims = new List<MetadataClaim>
        {
            MakeClaim("title", "Unknown Provider Value", unknownProvider, 0.9),
        };
        // Empty weights map — provider not registered.
        var weights = new Dictionary<Guid, double>();

        var resolution = resolver.Resolve("title", claims, weights, DefaultConfig);

        Assert.Equal("Unknown Provider Value", resolution.WinningClaim.ClaimValue);
        Assert.Equal(1.0, resolution.AdjustedConfidence);
    }

    // ── Empty claims → ArgumentException ─────────────────────────────────────

    [Fact]
    public void EmptyClaims_ThrowsArgumentException()
    {
        var resolver = CreateResolver();

        Assert.Throws<ArgumentException>(() =>
            resolver.Resolve("title", [], new Dictionary<Guid, double>(), DefaultConfig));
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
