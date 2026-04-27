using MediaEngine.Web.Components.Browse;

namespace MediaEngine.Web.Tests;

public sealed class BrowseArtworkRulesTests
{
    [Fact]
    public void ResolveWideArtwork_PrefersBackgroundOverBanner()
    {
        Assert.Equal("background.jpg", BrowseArtworkRules.ResolveWideArtwork("background.jpg", "banner.jpg"));
        Assert.Equal("banner.jpg", BrowseArtworkRules.ResolveWideArtwork(" ", "banner.jpg"));
    }

    [Fact]
    public void ResolveArtworkAspectRatio_UsesKnownDimensionsBeforeUrlHeuristics()
    {
        Assert.Equal("16 / 9", BrowseArtworkRules.ResolveArtworkAspectRatio("album.jpg", 16, 9));
        Assert.Equal("600 / 600", BrowseArtworkRules.ResolveArtworkAspectRatio(null, null, null, 600, 600));
        Assert.Equal("1 / 1", BrowseArtworkRules.ResolveArtworkAspectRatio("/images/album-cover.jpg", null, null));
        Assert.Equal("2 / 3", BrowseArtworkRules.ResolveArtworkAspectRatio("/images/poster.jpg", null, null));
    }

    [Fact]
    public void CompactFacts_RemovesBlankAndDuplicateValues()
    {
        var facts = BrowseArtworkRules.CompactFacts("  2024 ", null, "Action", "action", "", "HD");

        Assert.Equal(["2024", "Action", "HD"], facts);
    }

    [Theory]
    [InlineData("movie", "wide.jpg", null, "landscape")]
    [InlineData("music", null, null, "square")]
    [InlineData("book", null, "artist.jpg", "square")]
    [InlineData("book", null, null, "portrait")]
    public void PreferredDisplayShape_TracksMediaTypeAndArtwork(string mediaType, string? wideArtwork, string? squareArtwork, string expected)
    {
        Assert.Equal(expected, BrowseArtworkRules.PreferredDisplayShape(mediaType, wideArtwork, squareArtwork));
    }
}
