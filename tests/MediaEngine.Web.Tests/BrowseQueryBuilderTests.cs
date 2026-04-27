using MediaEngine.Web.Components.Browse;
using MediaEngine.Web.Models.ViewDTOs;
using MudBlazor;

namespace MediaEngine.Web.Tests;

public sealed class BrowseQueryBuilderTests
{
    [Fact]
    public void Read_NormalizesTabGroupingLayoutSortAndGroupState()
    {
        var preset = CreatePreset();
        var state = BrowseQueryBuilder.Read(
            preset,
            "music",
            "http://localhost/listen/music?grouping=albums&layout=list&sort=title&search=dune&group=aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa&groupType=album&groupName=Dune%20OST&groupField=album&groupMediaType=Music");

        Assert.Equal("music", state.ActiveTabId);
        Assert.Equal("albums", state.Grouping);
        Assert.Equal(LibraryLayoutMode.List, state.Layout);
        Assert.Equal("title", state.SortBy);
        Assert.Equal("dune", state.SearchText);
        Assert.True(state.IsGroupDrilldown);
        Assert.Equal(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), state.GroupId);
        Assert.Equal("album", state.GroupType);
        Assert.Equal("Dune OST", state.GroupName);
    }

    [Fact]
    public void Read_InvalidQueryFallsBackToTabDefaults()
    {
        var preset = CreatePreset();
        var state = BrowseQueryBuilder.Read(
            preset,
            "missing",
            "http://localhost/listen?grouping=bad&layout=bad&sort=bad");

        Assert.Equal("books", state.ActiveTabId);
        Assert.Equal("all", state.Grouping);
        Assert.Equal(LibraryLayoutMode.Card, state.Layout);
        Assert.Equal("newest", state.SortBy);
        Assert.False(state.IsGroupDrilldown);
    }

    private static LibraryBrowsePreset CreatePreset() => new()
    {
        RouteBase = "/listen",
        Title = "Listen",
        HeroVariant = BrowseHeroVariant.Listen,
        Tabs =
        [
            new()
            {
                Id = "books",
                Label = "Books",
                MediaType = "Books",
                DefaultGrouping = "all",
                DefaultLayout = LibraryLayoutMode.Card,
                GroupingOptions = [new("all", "All", Icons.Material.Outlined.LibraryBooks)],
            },
            new()
            {
                Id = "music",
                Label = "Music",
                MediaType = "Music",
                DefaultGrouping = "artists",
                DefaultLayout = LibraryLayoutMode.Card,
                GroupingOptions =
                [
                    new("artists", "Artists", Icons.Material.Outlined.Person),
                    new("albums", "Albums", Icons.Material.Outlined.Album),
                ],
            },
        ],
    };
}
