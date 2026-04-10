using MediaEngine.Domain.Entities;
using MediaEngine.Intelligence.Models;
using MediaEngine.Storage.Models;

namespace MediaEngine.Intelligence.Tests;

/// <summary>
/// Tests for the UserLockableFields restriction in <see cref="PriorityCascadeEngine"/>.
///
/// Only three fields accept user locks: "rating", "media_type", "custom_tags".
/// All other fields (title, author, genre, description, year, etc.) are resolved
/// exclusively by the provider hierarchy — user locks on those fields are silently ignored.
/// </summary>
public sealed class PriorityCascadeRestrictionTests
{
    private static readonly Guid WikidataProviderId = Guid.Parse("b3000003-d000-4000-8000-000000000004");
    private static readonly Guid ProviderA = Guid.Parse("aaaa0000-0000-0000-0000-000000000001");
    private static readonly Guid EntityId  = Guid.Parse("eeee0000-0000-0000-0000-000000000002");

    private static readonly ScoringConfiguration DefaultConfig = new();

    private static PriorityCascadeEngine CreateEngine() => new(new StubConfigurationLoader());

    // ── Lockable fields: user lock wins ──────────────────────────────────────

    [Fact]
    public async Task UserLock_OnRating_Wins()
    {
        var engine = CreateEngine();
        var lockedRating = MakeClaim("rating", "5", ProviderA, 0.5);
        lockedRating.IsUserLocked = true;

        var context = new ScoringContext
        {
            EntityId = EntityId,
            Claims =
            [
                MakeClaim("rating", "3", ProviderA, 1.0),   // High-confidence non-locked
                lockedRating,
            ],
            ProviderWeights = new Dictionary<Guid, double> { [ProviderA] = 1.0 },
            Configuration = DefaultConfig,
        };

        var result = await engine.ScoreEntityAsync(context);

        var ratingScore = result.FieldScores.First(f => f.Key == "rating");
        Assert.Equal("5", ratingScore.WinningValue);
        Assert.Equal(1.0, ratingScore.Confidence);
    }

    [Fact]
    public async Task UserLock_OnMediaType_Wins()
    {
        var engine = CreateEngine();
        var lockedMediaType = MakeClaim("media_type", "AUDIOBOOK", ProviderA, 0.4);
        lockedMediaType.IsUserLocked = true;

        var context = new ScoringContext
        {
            EntityId = EntityId,
            Claims =
            [
                MakeClaim("media_type", "EPUB", ProviderA, 1.0),
                lockedMediaType,
            ],
            ProviderWeights = new Dictionary<Guid, double> { [ProviderA] = 1.0 },
            Configuration = DefaultConfig,
        };

        var result = await engine.ScoreEntityAsync(context);

        var mediaTypeScore = result.FieldScores.First(f => f.Key == "media_type");
        Assert.Equal("AUDIOBOOK", mediaTypeScore.WinningValue);
    }

    [Fact]
    public async Task UserLock_OnCustomTags_Wins()
    {
        var engine = CreateEngine();
        var lockedTags = MakeClaim("custom_tags", "favourites", ProviderA, 0.3);
        lockedTags.IsUserLocked = true;

        var context = new ScoringContext
        {
            EntityId = EntityId,
            Claims =
            [
                MakeClaim("custom_tags", "to-read", ProviderA, 0.9),
                lockedTags,
            ],
            ProviderWeights = new Dictionary<Guid, double> { [ProviderA] = 1.0 },
            Configuration = DefaultConfig,
        };

        var result = await engine.ScoreEntityAsync(context);

        var tagsScore = result.FieldScores.First(f => f.Key == "custom_tags");
        Assert.Equal("favourites", tagsScore.WinningValue);
    }

    // ── Non-lockable fields: user lock is ignored, highest confidence wins ───

    [Fact]
    public async Task UserLock_OnTitle_Ignored_HighestConfidenceWins()
    {
        // "title" is NOT in UserLockableFields — user lock is silently ignored.
        // The highest-confidence non-Wikidata claim should win.
        var engine = CreateEngine();
        var lockedClaim = MakeClaim("title", "My Custom Title", ProviderA, 0.5);
        lockedClaim.IsUserLocked = true;

        var context = new ScoringContext
        {
            EntityId = EntityId,
            Claims =
            [
                MakeClaim("title", "Dune", ProviderA, 0.9), // Higher confidence, no lock
                lockedClaim,
            ],
            ProviderWeights = new Dictionary<Guid, double> { [ProviderA] = 1.0 },
            Configuration = DefaultConfig,
        };

        var result = await engine.ScoreEntityAsync(context);

        var titleScore = result.FieldScores.First(f => f.Key == "title");
        // Lock is ignored; highest confidence ("Dune" at 0.9) wins
        Assert.Equal("Dune", titleScore.WinningValue);
    }

    [Fact]
    public async Task UserLock_OnAuthor_Ignored_HighestConfidenceWins()
    {
        var engine = CreateEngine();
        var lockedClaim = MakeClaim("author", "My Override Author", ProviderA, 0.3);
        lockedClaim.IsUserLocked = true;

        var context = new ScoringContext
        {
            EntityId = EntityId,
            Claims =
            [
                MakeClaim("author", "Frank Herbert", ProviderA, 0.95),
                lockedClaim,
            ],
            ProviderWeights = new Dictionary<Guid, double> { [ProviderA] = 1.0 },
            Configuration = DefaultConfig,
        };

        var result = await engine.ScoreEntityAsync(context);

        var authorScore = result.FieldScores.First(f => f.Key == "author");
        Assert.Equal("Frank Herbert", authorScore.WinningValue);
    }

    [Fact]
    public async Task UserLock_OnGenre_Ignored_HighestConfidenceWins()
    {
        var engine = CreateEngine();
        var lockedClaim = MakeClaim("genre", "Romance", ProviderA, 0.3);
        lockedClaim.IsUserLocked = true;

        var context = new ScoringContext
        {
            EntityId = EntityId,
            Claims =
            [
                MakeClaim("genre", "Science Fiction", ProviderA, 0.9),
                lockedClaim,
            ],
            ProviderWeights = new Dictionary<Guid, double> { [ProviderA] = 1.0 },
            Configuration = DefaultConfig,
        };

        var result = await engine.ScoreEntityAsync(context);

        var genreScore = result.FieldScores.First(f => f.Key == "genre");
        Assert.Equal("Science Fiction", genreScore.WinningValue);
    }

    [Fact]
    public async Task UserLock_OnDescription_Ignored_HighestConfidenceWins()
    {
        var engine = CreateEngine();
        var lockedClaim = MakeClaim("description", "My notes", ProviderA, 0.2);
        lockedClaim.IsUserLocked = true;

        var context = new ScoringContext
        {
            EntityId = EntityId,
            Claims =
            [
                MakeClaim("description", "A science fiction novel.", ProviderA, 0.85),
                lockedClaim,
            ],
            ProviderWeights = new Dictionary<Guid, double> { [ProviderA] = 1.0 },
            Configuration = DefaultConfig,
        };

        var result = await engine.ScoreEntityAsync(context);

        var descScore = result.FieldScores.First(f => f.Key == "description");
        Assert.Equal("A science fiction novel.", descScore.WinningValue);
    }

    // ── Wikidata always wins for structured fields ────────────────────────────

    [Fact]
    public async Task WikidataAlwaysWins_ForTitle_OverRetailAndLocks()
    {
        var engine = CreateEngine();
        var lockedClaim = MakeClaim("title", "Locked Title", ProviderA, 1.0);
        lockedClaim.IsUserLocked = true;

        var context = new ScoringContext
        {
            EntityId = EntityId,
            Claims =
            [
                lockedClaim,
                MakeClaim("title", "Retail Title", ProviderA, 1.0),
                MakeClaim("title", "Dune: Canonical", WikidataProviderId, 0.95),
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
        Assert.Equal("Dune: Canonical", titleScore.WinningValue);
        Assert.Equal(WikidataProviderId, titleScore.WinningProviderId);
    }

    [Fact]
    public async Task WikidataWins_ForAuthor_WhenHigherConfidence()
    {
        var engine = CreateEngine();
        var context = new ScoringContext
        {
            EntityId = EntityId,
            Claims =
            [
                MakeClaim("author", "Frank Herbert (retail)", ProviderA, 0.85),
                MakeClaim("author", "Frank Herbert", WikidataProviderId, 0.9),
            ],
            ProviderWeights = new Dictionary<Guid, double>
            {
                [ProviderA]          = 1.0,
                [WikidataProviderId] = 1.0,
            },
            Configuration = DefaultConfig,
        };

        var result = await engine.ScoreEntityAsync(context);

        var authorScore = result.FieldScores.First(f => f.Key == "author");
        Assert.Equal("Frank Herbert", authorScore.WinningValue);
        Assert.Equal(WikidataProviderId, authorScore.WinningProviderId);
    }

    [Fact]
    public async Task PenNameAuthor_HigherConfidenceEmbedded_WinsOverLowerWikidata()
    {
        // When ReconciliationAdapter's pen name safety net fires, it emits the
        // pen name at 0.95 (EmbeddedAuthor) and the P50 real name at 0.75
        // (WikidataAuthorRaw). The pen name must win because its confidence is
        // higher — this is the deliberate design of the reduced P50 confidence.
        var engine = CreateEngine();
        var context = new ScoringContext
        {
            EntityId = EntityId,
            Claims =
            [
                MakeClaim("author", "James S.A. Corey", ProviderA, 0.95),          // pen name from file
                MakeClaim("author", "Daniel Abraham", WikidataProviderId, 0.75),    // P50 real name
            ],
            ProviderWeights = new Dictionary<Guid, double>
            {
                [ProviderA]          = 1.0,
                [WikidataProviderId] = 1.0,
            },
            Configuration = DefaultConfig,
        };

        var result = await engine.ScoreEntityAsync(context);

        var authorScore = result.FieldScores.First(f => f.Key == "author");
        Assert.Equal("James S.A. Corey", authorScore.WinningValue);
        Assert.Equal(ProviderA, authorScore.WinningProviderId);
    }

    [Fact]
    public async Task Author_EqualConfidence_WikidataWins()
    {
        // When confidences are equal, Wikidata authority takes precedence.
        var engine = CreateEngine();
        var context = new ScoringContext
        {
            EntityId = EntityId,
            Claims =
            [
                MakeClaim("author", "Retail Author", ProviderA, 0.90),
                MakeClaim("author", "Wikidata Author", WikidataProviderId, 0.90),
            ],
            ProviderWeights = new Dictionary<Guid, double>
            {
                [ProviderA]          = 1.0,
                [WikidataProviderId] = 1.0,
            },
            Configuration = DefaultConfig,
        };

        var result = await engine.ScoreEntityAsync(context);

        var authorScore = result.FieldScores.First(f => f.Key == "author");
        Assert.Equal("Wikidata Author", authorScore.WinningValue);
        Assert.Equal(WikidataProviderId, authorScore.WinningProviderId);
    }

    [Fact]
    public async Task WikidataAlwaysWins_ForYear()
    {
        var engine = CreateEngine();
        var context = new ScoringContext
        {
            EntityId = EntityId,
            Claims =
            [
                MakeClaim("year", "1966", ProviderA, 1.0),       // Retail year
                MakeClaim("year", "1965", WikidataProviderId, 0.9), // Canonical year
            ],
            ProviderWeights = new Dictionary<Guid, double>
            {
                [ProviderA]          = 1.0,
                [WikidataProviderId] = 1.0,
            },
            Configuration = DefaultConfig,
        };

        var result = await engine.ScoreEntityAsync(context);

        var yearScore = result.FieldScores.First(f => f.Key == "year");
        Assert.Equal("1965", yearScore.WinningValue);
        Assert.Equal(WikidataProviderId, yearScore.WinningProviderId);
    }

    // ── Edge cases ────────────────────────────────────────────────────────────

    [Fact]
    public async Task UserLock_OnRating_NoOtherClaims_LockWins()
    {
        var engine = CreateEngine();
        var lockedRating = MakeClaim("rating", "4", ProviderA, 0.8);
        lockedRating.IsUserLocked = true;

        var context = new ScoringContext
        {
            EntityId = EntityId,
            Claims = [lockedRating],
            ProviderWeights = new Dictionary<Guid, double> { [ProviderA] = 1.0 },
            Configuration = DefaultConfig,
        };

        var result = await engine.ScoreEntityAsync(context);

        var ratingScore = result.FieldScores.First(f => f.Key == "rating");
        Assert.Equal("4", ratingScore.WinningValue);
        Assert.Equal(1.0, ratingScore.Confidence);
    }

    [Fact]
    public async Task MultipleLockedRatings_MostRecentWins()
    {
        var engine = CreateEngine();

        var older = MakeClaim("rating", "3", ProviderA, 1.0);
        older.IsUserLocked = true;
        older.ClaimedAt = DateTimeOffset.UtcNow.AddDays(-5);

        var newer = MakeClaim("rating", "5", ProviderA, 1.0);
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

        var ratingScore = result.FieldScores.First(f => f.Key == "rating");
        Assert.Equal("5", ratingScore.WinningValue);
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
