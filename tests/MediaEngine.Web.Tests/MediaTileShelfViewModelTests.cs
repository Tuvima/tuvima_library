using MediaEngine.Web.Models.ViewDTOs;

namespace MediaEngine.Web.Tests;

public sealed class MediaTileShelfViewModelTests
{
    [Theory]
    [InlineData(MediaTileShelfKeys.Continue, MediaTileShelfKind.Continue)]
    [InlineData(MediaTileShelfKeys.ContinueReading, MediaTileShelfKind.Continue)]
    [InlineData(MediaTileShelfKeys.ContinueWatching, MediaTileShelfKind.Continue)]
    [InlineData(MediaTileShelfKeys.ContinueListening, MediaTileShelfKind.Continue)]
    [InlineData(MediaTileShelfKeys.Collections, MediaTileShelfKind.Collections)]
    [InlineData(MediaTileShelfKeys.HomeCollections, MediaTileShelfKind.Collections)]
    [InlineData(MediaTileShelfKeys.ListenCollections, MediaTileShelfKind.Collections)]
    [InlineData(MediaTileShelfKeys.ReadSeries, MediaTileShelfKind.ReadSeries)]
    [InlineData("recently-added", MediaTileShelfKind.Standard)]
    public void Kind_ClassifiesStableDisplayKey(string key, MediaTileShelfKind expected)
    {
        var shelf = new MediaTileShelfViewModel { Key = key };

        Assert.Equal(expected, shelf.Kind);
    }

    [Fact]
    public void Kind_DoesNotInferBehaviorFromDisplayCopyOrRoute()
    {
        var shelf = new MediaTileShelfViewModel
        {
            Key = "recently-added",
            Title = "Continue reading Collections",
            Subtitle = "Series & Reading Lists",
            SeeAllRoute = "/collections?grouping=series",
        };

        Assert.Equal(MediaTileShelfKind.Standard, shelf.Kind);
    }

    [Fact]
    public void HubShelf_PreservesKeyAndDerivesContinueBehaviorFromIt()
    {
        var source = new MediaTileShelfViewModel
        {
            Key = MediaTileShelfKeys.ContinueListening,
            Title = "Resume audio",
        };

        var hubShelf = MediaHubShelfViewModel.FromShelf(source);
        var roundTrip = hubShelf.ToMediaTileShelf();

        Assert.Equal(MediaTileShelfKeys.ContinueListening, hubShelf.Key);
        Assert.True(hubShelf.IsContinueShelf);
        Assert.Equal(MediaTileShelfKeys.ContinueListening, roundTrip.Key);
        Assert.Equal(MediaTileShelfKind.Continue, roundTrip.Kind);
    }
}
