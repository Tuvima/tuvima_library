using MediaEngine.Domain;
using MediaEngine.Domain.Constants;
using MediaEngine.Domain.Enums;
using MediaEngine.Intelligence.Strategies;

namespace MediaEngine.Intelligence.Tests;

public sealed class MediaTypeIdentityProfileCatalogTests
{
    public static TheoryData<
        MediaType,
        string[],
        string[],
        bool,
        double,
        bool> Profiles => new()
    {
        {
            MediaType.Books,
            [BridgeIdKeys.Isbn, BridgeIdKeys.OpenLibraryId],
            [MetadataFieldConstants.Author, MetadataFieldConstants.Title],
            true,
            ConfidenceBand.StrongFloor,
            true
        },
        {
            MediaType.Audiobooks,
            [BridgeIdKeys.Isbn, BridgeIdKeys.Asin, BridgeIdKeys.AppleMusicId],
            [MetadataFieldConstants.Author, MetadataFieldConstants.Narrator, MetadataFieldConstants.Title],
            true,
            ConfidenceBand.StrongFloor,
            true
        },
        {
            MediaType.Movies,
            [BridgeIdKeys.TmdbId],
            [MetadataFieldConstants.Title, MetadataFieldConstants.Year],
            true,
            ConfidenceBand.StrongFloor,
            false
        },
        {
            MediaType.TV,
            [BridgeIdKeys.TmdbId, BridgeIdKeys.TvdbId],
            [
                MetadataFieldConstants.ShowName,
                MetadataFieldConstants.SeasonNumber,
                MetadataFieldConstants.EpisodeNumber,
            ],
            true,
            ConfidenceBand.StrongFloor,
            false
        },
        {
            MediaType.Music,
            [
                BridgeIdKeys.AppleMusicId,
                BridgeIdKeys.AppleMusicCollectionId,
                BridgeIdKeys.MusicBrainzId,
            ],
            [
                MetadataFieldConstants.Artist,
                MetadataFieldConstants.Album,
                MetadataFieldConstants.Title,
            ],
            false,
            ConfidenceBand.StrongFloor,
            false
        },
        {
            MediaType.Comics,
            ["comicvine_id"],
            [
                MetadataFieldConstants.Series,
                MetadataFieldConstants.IssueNumber,
                MetadataFieldConstants.Title,
            ],
            true,
            ConfidenceBand.StrongFloor,
            false
        },
    };

    [Theory]
    [MemberData(nameof(Profiles))]
    public void Catalog_PinsEveryProfileValue(
        MediaType mediaType,
        string[] bridgeIds,
        string[] criticalFields,
        bool allowsTextFallback,
        double minimumConfidence,
        bool requiresCreator)
    {
        var profile = MediaTypeIdentityProfileCatalog.Get(mediaType);

        Assert.Equal(mediaType, profile.MediaType);
        Assert.Equal(bridgeIds, profile.PreferredBridgeIds);
        Assert.Equal(criticalFields, profile.CriticalFields);
        Assert.Equal(allowsTextFallback, profile.AllowsTextFallback);
        Assert.Equal(minimumConfidence, profile.TextFallbackMinConfidence);
        Assert.Equal(requiresCreator, profile.RequiresCreatorForFallback);
    }

    [Fact]
    public void Catalog_ContainsExactlyOneProfilePerSupportedMediaType()
    {
        Assert.Equal(6, MediaTypeIdentityProfileCatalog.All.Count);
        Assert.Equal(
            MediaTypeIdentityProfileCatalog.All.Count,
            MediaTypeIdentityProfileCatalog.All.Select(profile => profile.MediaType).Distinct().Count());
    }

    [Fact]
    public void Catalog_GetRejectsUnsupportedMediaType()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => MediaTypeIdentityProfileCatalog.Get((MediaType)int.MaxValue));
    }
}
