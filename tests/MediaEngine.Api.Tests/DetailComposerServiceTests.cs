using MediaEngine.Api.Services.Details;
using MediaEngine.Contracts.Details;

namespace MediaEngine.Api.Tests;

public sealed class DetailComposerServiceTests
{
    [Fact]
    public void TryParseEntityType_DoesNotExposePodcastTypes()
    {
        Assert.False(DetailComposerService.TryParseEntityType("podcast", out _));
        Assert.False(DetailComposerService.TryParseEntityType("podcast-episode", out _));
    }

    [Fact]
    public void TryParseEntityType_ParsesSupportedKebabCaseTypes()
    {
        Assert.True(DetailComposerService.TryParseEntityType("tv-show", out var entityType));
        Assert.Equal(DetailEntityType.TvShow, entityType);
    }

    [Theory]
    [InlineData("listen", DetailPresentationContext.Listen)]
    [InlineData("watch", DetailPresentationContext.Watch)]
    [InlineData("read", DetailPresentationContext.Read)]
    [InlineData("unknown", DetailPresentationContext.Default)]
    public void ParseContext_UsesDefaultForUnknownValues(string value, DetailPresentationContext expected)
    {
        Assert.Equal(expected, DetailComposerService.ParseContext(value));
    }

    [Fact]
    public void ResolveArtworkPresentationMode_PrioritizesRealBackdrops()
    {
        var mode = DetailComposerService.ResolveArtworkPresentationMode(
            DetailEntityType.Movie,
            backdropUrl: "/backdrop.jpg",
            bannerUrl: null,
            coverUrl: "/cover.jpg",
            posterUrl: null,
            portraitUrl: null,
            relatedArtworkCount: 0,
            ownedFormatCount: 1);

        Assert.Equal(ArtworkPresentationMode.CinematicBackdrop, mode);
    }

    [Fact]
    public void ResolveArtworkPresentationMode_UsesCoverGradientWithoutBackdrop()
    {
        var mode = DetailComposerService.ResolveArtworkPresentationMode(
            DetailEntityType.Book,
            backdropUrl: null,
            bannerUrl: null,
            coverUrl: "/cover.jpg",
            posterUrl: null,
            portraitUrl: null,
            relatedArtworkCount: 0,
            ownedFormatCount: 1);

        Assert.Equal(ArtworkPresentationMode.ColorGradientFromArtwork, mode);
    }

    [Fact]
    public void ResolveArtworkPresentationMode_UsesPairedEditionGradientForMultiFormatWorks()
    {
        var mode = DetailComposerService.ResolveArtworkPresentationMode(
            DetailEntityType.Work,
            backdropUrl: null,
            bannerUrl: null,
            coverUrl: "/ebook.jpg",
            posterUrl: null,
            portraitUrl: null,
            relatedArtworkCount: 0,
            ownedFormatCount: 2);

        Assert.Equal(ArtworkPresentationMode.PairedEditionGradient, mode);
    }

    [Fact]
    public void ResolveArtworkPresentationMode_UsesPortraitEchoForPeople()
    {
        var mode = DetailComposerService.ResolveArtworkPresentationMode(
            DetailEntityType.Person,
            backdropUrl: null,
            bannerUrl: null,
            coverUrl: null,
            posterUrl: null,
            portraitUrl: "/portrait.jpg",
            relatedArtworkCount: 0,
            ownedFormatCount: 0);

        Assert.Equal(ArtworkPresentationMode.PortraitEcho, mode);
    }
}
