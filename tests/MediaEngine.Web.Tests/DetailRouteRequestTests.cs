using MediaEngine.Contracts.Details;
using MediaEngine.Web.Components.Details;

namespace MediaEngine.Web.Tests;

public sealed class DetailRouteRequestTests
{
    private static readonly Guid EntityId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid ContainerId = Guid.Parse("20000000-0000-0000-0000-000000000002");

    [Theory]
    [InlineData(null, DetailPresentationContext.Read)]
    [InlineData("read", DetailPresentationContext.Read)]
    [InlineData("LISTEN", DetailPresentationContext.Listen)]
    public void BookRoute_UsesCanonicalWorkWithLaneContext(
        string? mode,
        DetailPresentationContext expectedContext)
    {
        var request = DetailRouteRequest.ForBook(EntityId, mode);

        Assert.Equal(DetailEntityType.Work, request.EntityType);
        Assert.Equal(EntityId, request.EntityId);
        Assert.Equal(expectedContext, request.PresentationContext);
        Assert.Null(request.ContainerId);
        Assert.Equal("Book not found", request.NotFoundTitle);
        Assert.Equal("Book", request.PageTitleFallback);
        Assert.Equal("Tuvima Library", request.ProductTitle);
    }

    [Fact]
    public void MovieRoute_UsesWatchContext()
    {
        var request = DetailRouteRequest.ForMovie(EntityId);

        Assert.Equal(DetailEntityType.Movie, request.EntityType);
        Assert.Equal(EntityId, request.EntityId);
        Assert.Equal(DetailPresentationContext.Watch, request.PresentationContext);
        Assert.Equal("Movie", request.PageTitleFallback);
        Assert.Equal("Tuvima", request.ProductTitle);
    }

    [Fact]
    public void TvShowRoute_LoadsRootShowWithoutAContainer()
    {
        var request = DetailRouteRequest.ForTvShow(ContainerId, episodeId: null);

        Assert.Equal(DetailEntityType.TvShow, request.EntityType);
        Assert.Equal(ContainerId, request.EntityId);
        Assert.Equal(DetailPresentationContext.Watch, request.PresentationContext);
        Assert.Null(request.ContainerId);
        Assert.Equal("TV show not found", request.NotFoundTitle);
        Assert.Equal("TV Show", request.PageTitleFallback);
        Assert.Equal("Tuvima", request.ProductTitle);
    }

    [Fact]
    public void TvEpisodeRoute_PreservesItsShowContainer()
    {
        var request = DetailRouteRequest.ForTvShow(ContainerId, EntityId);

        Assert.Equal(DetailEntityType.TvEpisode, request.EntityType);
        Assert.Equal(EntityId, request.EntityId);
        Assert.Equal(ContainerId.ToString("D"), request.ContainerId);
        Assert.Equal("Episode not found", request.NotFoundTitle);
    }

    [Theory]
    [InlineData("music-album", "listen", DetailEntityType.MusicAlbum, DetailPresentationContext.Listen, "Back to Listen", "/listen")]
    [InlineData("comic_issue", "comics", DetailEntityType.ComicIssue, DetailPresentationContext.Comics, "Back to Read", "/read")]
    [InlineData("movie", "watch", DetailEntityType.Movie, DetailPresentationContext.Watch, "Back to Watch", "/watch")]
    [InlineData("person", "not-a-context", DetailEntityType.Person, DetailPresentationContext.Default, "Back", "/")]
    public void UnifiedRoute_ParsesEntityContextAndOrigin(
        string entityType,
        string context,
        DetailEntityType expectedEntityType,
        DetailPresentationContext expectedContext,
        string expectedBackLabel,
        string expectedFallbackRoute)
    {
        var request = DetailRouteRequest.ForUnified(entityType, EntityId, context);

        Assert.True(request.CanLoad);
        Assert.Equal(expectedEntityType, request.EntityType);
        Assert.Equal(expectedContext, request.PresentationContext);
        Assert.Equal(expectedBackLabel, request.OriginNavigation?.Label);
        Assert.Equal(expectedFallbackRoute, request.OriginNavigation?.FallbackRoute);
        Assert.Equal("Details", request.PageTitleFallback);
        Assert.Equal("Tuvima Library", request.ProductTitle);
    }

    [Theory]
    [InlineData("tv-episode")]
    [InlineData("TvEpisode")]
    [InlineData("podcast")]
    [InlineData("PodcastSeries")]
    [InlineData("not-real")]
    public void UnifiedRoute_RejectsUnsupportedEntities(string entityType)
    {
        var request = DetailRouteRequest.ForUnified(entityType, EntityId, "watch");

        Assert.False(request.CanLoad);
        Assert.Null(request.EntityType);
        Assert.Equal("Detail page not found", request.NotFoundTitle);
    }
}
