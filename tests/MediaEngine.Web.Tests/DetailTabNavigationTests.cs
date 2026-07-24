using MediaEngine.Contracts.Details;
using MediaEngine.Web.Models.ViewDTOs;
using MediaEngine.Web.Services.Navigation;

namespace MediaEngine.Web.Tests;

public sealed class DetailTabNavigationTests
{
    [Fact]
    public void Resolve_NoRequestedTab_UsesFirstVisibleTab()
    {
        var model = CreateModel("overview", "cast", "details");

        var resolution = DetailTabNavigation.Resolve(model, null);

        Assert.Equal("overview", resolution.ActiveTab);
        Assert.False(resolution.HasRequestedTab);
        Assert.False(resolution.ShouldRedirect);
    }

    [Fact]
    public void Resolve_RequestedTab_IsCaseInsensitiveAndRedirectsToCanonicalSlug()
    {
        var model = CreateModel(["overview", "cast", "details"], withCast: true);

        var resolution = DetailTabNavigation.Resolve(model, "CAST");

        Assert.Equal("cast", resolution.ActiveTab);
        Assert.True(resolution.IsRequestedTabValid);
        Assert.True(resolution.ShouldRedirect);
    }

    [Fact]
    public void Resolve_InvalidTab_RedirectsToFirstVisibleTab()
    {
        var model = CreateModel("overview", "details");

        var resolution = DetailTabNavigation.Resolve(model, "missing");

        Assert.Equal("overview", resolution.ActiveTab);
        Assert.False(resolution.IsRequestedTabValid);
        Assert.True(resolution.ShouldRedirect);
    }

    [Fact]
    public void Resolve_CastTabWithoutRenderableCredits_FallsBack()
    {
        var model = CreateModel("overview", "cast", "details");

        var resolution = DetailTabNavigation.Resolve(model, "cast");

        Assert.Equal("overview", resolution.ActiveTab);
        Assert.False(resolution.IsRequestedTabValid);
        Assert.True(resolution.ShouldRedirect);
    }

    [Fact]
    public void Resolve_SeriesTabUsesSequencePlacementAndRemainsFirst()
    {
        var model = new DetailPageViewModel
        {
            Id = Guid.NewGuid().ToString("D"),
            EntityType = DetailEntityType.Book,
            Title = "Leviathan Wakes",
            Tabs = [
                new DetailTab { Key = "series", Label = "Series" },
                new DetailTab { Key = "overview", Label = "Overview" },
                new DetailTab { Key = "details", Label = "Details" },
            ],
            SequencePlacement = new SequencePlacementViewModel
            {
                ContainerId = Guid.NewGuid().ToString("D"),
                ContainerTitle = "The Expanse",
                PositionSummary = "Book 1 in The Expanse",
            },
        };

        var resolution = DetailTabNavigation.Resolve(model, null);

        Assert.Equal("series", resolution.ActiveTab);
        Assert.False(resolution.ShouldRedirect);
    }

    [Fact]
    public void MediaNavigation_DoesNotCreateDetailTabUrls()
    {
        var workId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var collectionId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        var route = MediaNavigation.ForMedia("Movies", workId, collectionId, "cast");

        Assert.Equal(
            "/details/movie/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa?context=watch",
            route);
        Assert.DoesNotContain("tab=", route, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MediaNavigation_OpensTracksAndAlbumsOnTheirOwnCanonicalDetails()
    {
        var trackId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var albumId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        Assert.Equal(
            $"/details/musictrack/{trackId:D}?context=listen",
            MediaNavigation.ForMedia("Music", trackId, albumId));
        Assert.Equal(
            $"/details/musicalbum/{albumId:D}?context=listen",
            MediaNavigation.ForCollectionMedia("Music", albumId));
    }

    [Fact]
    public void MediaNavigation_UsesRootWorkIdForTvContentGroups()
    {
        var collectionId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var rootWorkId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var group = new ContentGroupViewModel
        {
            CollectionId = collectionId,
            RootWorkId = rootWorkId,
            PrimaryMediaType = "TV",
        };

        Assert.Equal($"/watch/tv/show/{rootWorkId:D}", MediaNavigation.ForContentGroup(group));
    }

    [Fact]
    public void MediaNavigation_UsesRootWorkIdForMusicAlbumContentGroups()
    {
        var collectionId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var rootWorkId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var group = new ContentGroupViewModel
        {
            CollectionId = collectionId,
            RootWorkId = rootWorkId,
            PrimaryMediaType = "Music",
        };

        Assert.Equal(
            $"/details/musicalbum/{rootWorkId:D}?context=listen",
            MediaNavigation.ForContentGroup(group));
    }

    [Fact]
    public void MediaNavigation_KeepsCollectionFallbackForTvContentGroupsWithoutRoot()
    {
        var collectionId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var group = new ContentGroupViewModel
        {
            CollectionId = collectionId,
            PrimaryMediaType = "TV",
        };

        Assert.Equal($"/watch/tv/show/{collectionId:D}", MediaNavigation.ForContentGroup(group));
    }

    private static DetailPageViewModel CreateModel(params string[] tabs) =>
        CreateModel(tabs, withCast: false);

    private static DetailPageViewModel CreateModel(string[] tabs, bool withCast) =>
        new()
        {
            Id = Guid.NewGuid().ToString("D"),
            EntityType = DetailEntityType.Movie,
            Title = "Test",
            Tabs = tabs.Select(tab => new DetailTab { Key = tab, Label = tab }).ToList(),
            ContributorGroups = withCast
                ? [new CreditGroupViewModel { Title = "Cast", GroupType = CreditGroupType.Cast }]
                : [],
        };
}
