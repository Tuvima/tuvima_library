using MediaEngine.Domain;
using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Storage.Services;

namespace MediaEngine.Storage.Tests;

public class WorkClaimRouterTests
{
    private readonly WorkClaimRouter _router = new();

    private static WorkLineage MusicTrackLineage(out Guid trackWorkId, out Guid albumWorkId)
    {
        var assetId   = Guid.NewGuid();
        var editionId = Guid.NewGuid();
        trackWorkId   = Guid.NewGuid();
        albumWorkId   = Guid.NewGuid();
        return new WorkLineage(
            AssetId:          assetId,
            EditionId:        editionId,
            WorkId:           trackWorkId,
            ParentWorkId:     albumWorkId,
            RootParentWorkId: albumWorkId,
            WorkKind:         WorkKind.Child,
            MediaType:        MediaType.Music);
    }

    private static WorkLineage TVEpisodeLineage(
        out Guid episodeWorkId, out Guid seasonWorkId, out Guid showWorkId)
    {
        var assetId   = Guid.NewGuid();
        var editionId = Guid.NewGuid();
        episodeWorkId = Guid.NewGuid();
        seasonWorkId  = Guid.NewGuid();
        showWorkId    = Guid.NewGuid();
        return new WorkLineage(
            AssetId:          assetId,
            EditionId:        editionId,
            WorkId:           episodeWorkId,
            ParentWorkId:     seasonWorkId,   // immediate parent
            RootParentWorkId: showWorkId,     // topmost (Show)
            WorkKind:         WorkKind.Child,
            MediaType:        MediaType.TV);
    }

    private static WorkLineage MovieLineage(out Guid workId)
    {
        workId = Guid.NewGuid();
        return new WorkLineage(
            AssetId:          Guid.NewGuid(),
            EditionId:        Guid.NewGuid(),
            WorkId:           workId,
            ParentWorkId:     null,
            RootParentWorkId: workId,    // standalone collapses to self
            WorkKind:         WorkKind.Standalone,
            MediaType:        MediaType.Movies);
    }

    // ── Single-claim routing ─────────────────────────────────────────────

    [Fact]
    public void Music_TrackTitle_RoutesToTrackWork()
    {
        var lineage = MusicTrackLineage(out var trackId, out _);
        Assert.Equal(trackId, _router.Route(lineage, MetadataFieldConstants.Title));
    }

    [Fact]
    public void Music_AlbumName_RoutesToAlbumWork()
    {
        var lineage = MusicTrackLineage(out _, out var albumId);
        Assert.Equal(albumId, _router.Route(lineage, MetadataFieldConstants.Album));
    }

    [Fact]
    public void TV_ShowName_OnEpisode_RoutesToShow_NotSeason()
    {
        var lineage = TVEpisodeLineage(out _, out var seasonId, out var showId);
        var target = _router.Route(lineage, MetadataFieldConstants.ShowName);
        Assert.Equal(showId, target);
        Assert.NotEqual(seasonId, target);
    }

    [Fact]
    public void TV_EpisodeNumber_RoutesToEpisode()
    {
        var lineage = TVEpisodeLineage(out var episodeId, out _, out _);
        Assert.Equal(episodeId, _router.Route(lineage, MetadataFieldConstants.EpisodeNumber));
    }

    [Fact]
    public void Movie_AnyClaim_RoutesToMovieWork()
    {
        // Standalone — both Self and Parent collapse to the same id.
        var lineage = MovieLineage(out var workId);
        Assert.Equal(workId, _router.Route(lineage, MetadataFieldConstants.Title));
        Assert.Equal(workId, _router.Route(lineage, MetadataFieldConstants.Year));
        Assert.Equal(workId, _router.Route(lineage, MetadataFieldConstants.Genre));
    }

    // ── Bridge ID splitting ──────────────────────────────────────────────

    [Fact]
    public void SplitBridgeIds_Music_SplitsTrackVsAlbumIds()
    {
        var lineage = MusicTrackLineage(out _, out _);
        var input = new Dictionary<string, string>
        {
            [BridgeIdKeys.AppleMusicId]           = "12345",
            [BridgeIdKeys.AppleMusicCollectionId] = "99999",
            [BridgeIdKeys.AppleArtistId]          = "55555",
        };

        var (forParent, forSelf) = _router.SplitBridgeIds(lineage, input);

        Assert.Equal(2, forParent.Count);
        Assert.True(forParent.ContainsKey(BridgeIdKeys.AppleMusicCollectionId));
        Assert.True(forParent.ContainsKey(BridgeIdKeys.AppleArtistId));

        Assert.Single(forSelf);
        Assert.True(forSelf.ContainsKey(BridgeIdKeys.AppleMusicId));
    }

    [Fact]
    public void SplitBridgeIds_IgnoresEmptyValues()
    {
        var lineage = MusicTrackLineage(out _, out _);
        var input = new Dictionary<string, string>
        {
            [BridgeIdKeys.AppleMusicId]           = "12345",
            [BridgeIdKeys.AppleMusicCollectionId] = "  ",     // whitespace
            [BridgeIdKeys.MusicBrainzId]          = "",       // empty
        };

        var (forParent, forSelf) = _router.SplitBridgeIds(lineage, input);

        Assert.Empty(forParent);
        Assert.Single(forSelf);
    }

    // ── Claim batch splitting ────────────────────────────────────────────

    [Fact]
    public void SplitClaims_Music_RewritesEntityIds()
    {
        var lineage = MusicTrackLineage(out var trackId, out var albumId);

        var providerId = Guid.NewGuid();
        var assetId    = lineage.AssetId;

        var claims = new List<MetadataClaim>
        {
            new() { Id = Guid.NewGuid(), EntityId = assetId, ProviderId = providerId,
                    ClaimKey = MetadataFieldConstants.Title,  ClaimValue = "Cuff It" },
            new() { Id = Guid.NewGuid(), EntityId = assetId, ProviderId = providerId,
                    ClaimKey = MetadataFieldConstants.Album,  ClaimValue = "Renaissance" },
            new() { Id = Guid.NewGuid(), EntityId = assetId, ProviderId = providerId,
                    ClaimKey = MetadataFieldConstants.Artist, ClaimValue = "Beyoncé" },
            new() { Id = Guid.NewGuid(), EntityId = assetId, ProviderId = providerId,
                    ClaimKey = MetadataFieldConstants.TrackNumber, ClaimValue = "3" },
        };

        var (forParent, forSelf) = _router.SplitClaims(lineage, claims);

        Assert.Equal(2, forParent.Count);
        Assert.All(forParent, c => Assert.Equal(albumId, c.EntityId));
        Assert.Contains(forParent, c => c.ClaimKey == MetadataFieldConstants.Album);
        Assert.Contains(forParent, c => c.ClaimKey == MetadataFieldConstants.Artist);

        Assert.Equal(2, forSelf.Count);
        Assert.All(forSelf, c => Assert.Equal(trackId, c.EntityId));
        Assert.Contains(forSelf, c => c.ClaimKey == MetadataFieldConstants.Title);
        Assert.Contains(forSelf, c => c.ClaimKey == MetadataFieldConstants.TrackNumber);
    }

    [Fact]
    public void SplitClaims_TV_RoutesShowFieldsToShow_NotSeason()
    {
        var lineage = TVEpisodeLineage(out var episodeId, out var seasonId, out var showId);
        var providerId = Guid.NewGuid();

        var claims = new List<MetadataClaim>
        {
            new() { Id = Guid.NewGuid(), EntityId = lineage.AssetId, ProviderId = providerId,
                    ClaimKey = MetadataFieldConstants.ShowName, ClaimValue = "Severance" },
            new() { Id = Guid.NewGuid(), EntityId = lineage.AssetId, ProviderId = providerId,
                    ClaimKey = MetadataFieldConstants.Network,  ClaimValue = "Apple TV+" },
            new() { Id = Guid.NewGuid(), EntityId = lineage.AssetId, ProviderId = providerId,
                    ClaimKey = MetadataFieldConstants.EpisodeNumber, ClaimValue = "1" },
        };

        var (forParent, forSelf) = _router.SplitClaims(lineage, claims);

        Assert.Equal(2, forParent.Count);
        Assert.All(forParent, c => Assert.Equal(showId, c.EntityId));
        Assert.All(forParent, c => Assert.NotEqual(seasonId, c.EntityId));

        Assert.Single(forSelf);
        Assert.Equal(episodeId, forSelf[0].EntityId);
    }

    [Fact]
    public void SplitClaims_Movie_AllClaimsLandOnSameWork()
    {
        var lineage = MovieLineage(out var workId);
        var providerId = Guid.NewGuid();

        var claims = new List<MetadataClaim>
        {
            new() { Id = Guid.NewGuid(), EntityId = lineage.AssetId, ProviderId = providerId,
                    ClaimKey = MetadataFieldConstants.Title, ClaimValue = "Dune" },
            new() { Id = Guid.NewGuid(), EntityId = lineage.AssetId, ProviderId = providerId,
                    ClaimKey = MetadataFieldConstants.Year,  ClaimValue = "2021" },
            new() { Id = Guid.NewGuid(), EntityId = lineage.AssetId, ProviderId = providerId,
                    ClaimKey = MetadataFieldConstants.Director, ClaimValue = "Denis Villeneuve" },
        };

        var (forParent, forSelf) = _router.SplitClaims(lineage, claims);
        var allEntityIds = forParent.Concat(forSelf).Select(c => c.EntityId).Distinct().ToList();
        Assert.Single(allEntityIds);
        Assert.Equal(workId, allEntityIds[0]);
    }
}
