using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;

namespace MediaEngine.Domain.Tests;

public class AggregateTests
{
    // ════════════════════════════════════════════════════════════════════════
    //  Hub
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Hub_DefaultState_HasEmptyCollections()
    {
        var hub = new Hub();

        Assert.Equal(Guid.Empty, hub.Id);
        Assert.Null(hub.UniverseId);
        Assert.Null(hub.DisplayName);
        Assert.Equal("Unknown", hub.UniverseStatus);
        Assert.Empty(hub.Works);
        Assert.Empty(hub.Relationships);
    }

    [Fact]
    public void Hub_WithWorks_TracksChildren()
    {
        var hub = new Hub { Id = Guid.NewGuid(), DisplayName = "Dune" };
        hub.Works.Add(new Work { Id = Guid.NewGuid(), HubId = hub.Id, MediaType = MediaType.Books });
        hub.Works.Add(new Work { Id = Guid.NewGuid(), HubId = hub.Id, MediaType = MediaType.Movies });

        Assert.Equal(2, hub.Works.Count);
        Assert.All(hub.Works, w => Assert.Equal(hub.Id, w.HubId));
    }

    [Fact]
    public void Hub_UniverseId_IsOptional()
    {
        var universeId = Guid.NewGuid();
        var hub = new Hub { Id = Guid.NewGuid(), UniverseId = universeId };

        Assert.Equal(universeId, hub.UniverseId);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Work
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Work_DefaultState_HasEmptyCollections()
    {
        var work = new Work();

        Assert.Equal(MediaType.Unknown, work.MediaType);
        Assert.Null(work.HubId);
        Assert.Equal(WorkKind.Standalone, work.WorkKind);
        Assert.Null(work.ParentWorkId);
        Assert.Null(work.Ordinal);
        Assert.False(work.IsCatalogOnly);
        Assert.Empty(work.ExternalIdentifiers);
        Assert.False(work.UniverseMismatch);
        Assert.Null(work.UniverseMismatchAt);
        Assert.Equal("pending", work.WikidataStatus);
        Assert.Empty(work.Editions);
        Assert.Empty(work.MetadataClaims);
        Assert.Empty(work.CanonicalValues);
    }

    [Fact]
    public void Work_Ordinal_ForSeriesOrdering()
    {
        var work = new Work
        {
            Id = Guid.NewGuid(),
            MediaType = MediaType.Books,
            Ordinal = 3,
        };

        Assert.Equal(3, work.Ordinal);
    }

    [Fact]
    public void Work_UniverseMismatch_TracksTimestamp()
    {
        var now = DateTimeOffset.UtcNow;
        var work = new Work
        {
            UniverseMismatch = true,
            UniverseMismatchAt = now,
            WikidataStatus = "skipped",
        };

        Assert.True(work.UniverseMismatch);
        Assert.Equal(now, work.UniverseMismatchAt);
        Assert.Equal("skipped", work.WikidataStatus);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  MediaAsset
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void MediaAsset_DefaultStatus_IsNormal()
    {
        var asset = new MediaAsset();

        Assert.Equal(AssetStatus.Normal, asset.Status);
        Assert.Equal(string.Empty, asset.ContentHash);
        Assert.Equal(string.Empty, asset.FilePathRoot);
        Assert.Null(asset.Manifest);
    }

    [Fact]
    public void MediaAsset_ContentHash_IsIdentity()
    {
        var hash = "abc123def456";
        var asset = new MediaAsset { ContentHash = hash, FilePathRoot = "/library/Books/test.epub" };

        Assert.Equal(hash, asset.ContentHash);
    }

    [Fact]
    public void MediaAsset_StatusValues_CoverAllStates()
    {
        Assert.Equal(AssetStatus.Normal, (AssetStatus)0);
        Assert.Equal(AssetStatus.Conflicted, (AssetStatus)1);
        Assert.Equal(AssetStatus.Orphaned, (AssetStatus)2);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  MetadataClaim
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void MetadataClaim_DefaultConfidence_IsOne()
    {
        var claim = new MetadataClaim();

        Assert.Equal(1.0, claim.Confidence);
        Assert.False(claim.IsUserLocked);
        Assert.Equal(string.Empty, claim.ClaimKey);
        Assert.Equal(string.Empty, claim.ClaimValue);
    }

    [Fact]
    public void MetadataClaim_ClaimedAt_DefaultsToNow()
    {
        var before = DateTimeOffset.UtcNow;
        var claim = new MetadataClaim();
        var after = DateTimeOffset.UtcNow;

        Assert.InRange(claim.ClaimedAt, before, after);
    }

    [Fact]
    public void MetadataClaim_UserLocked_CanBeSet()
    {
        var claim = new MetadataClaim
        {
            ClaimKey = "title",
            ClaimValue = "My Override",
            IsUserLocked = true,
            Confidence = 1.0,
        };

        Assert.True(claim.IsUserLocked);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  CanonicalValue
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void CanonicalValue_IsConflicted_DefaultsFalse()
    {
        var cv = new CanonicalValue();

        Assert.False(cv.IsConflicted);
        Assert.Equal(string.Empty, cv.Key);
        Assert.Equal(string.Empty, cv.Value);
    }

    [Fact]
    public void CanonicalValue_ConflictedField_CanBeSet()
    {
        var cv = new CanonicalValue
        {
            EntityId = Guid.NewGuid(),
            Key = "title",
            Value = "Ambiguous Title",
            IsConflicted = true,
            LastScoredAt = DateTimeOffset.UtcNow,
        };

        Assert.True(cv.IsConflicted);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  MediaType enum
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void MediaType_Unknown_IsDefaultZero()
    {
        Assert.Equal(0, (int)MediaType.Unknown);
    }

    [Theory]
    [InlineData(MediaType.Movies)]
    [InlineData(MediaType.Books)]
    [InlineData(MediaType.Audiobooks)]
    [InlineData(MediaType.Comics)]
    [InlineData(MediaType.TV)]
    [InlineData(MediaType.Music)]
    public void MediaType_AllValues_AreDefined(MediaType type)
    {
        Assert.True(Enum.IsDefined(type));
    }

    [Fact]
    public void MediaType_DefaultValue_IsUnknown()
    {
        var work = new Work();
        Assert.Equal(MediaType.Unknown, work.MediaType);
    }
}
