using MediaEngine.Web.Components.Browse;
using MediaEngine.Web.Models.ViewDTOs;

namespace MediaEngine.Web.Tests;

public sealed class LaneNavigationAndBrowseFilterTests
{
    [Fact]
    public void BrowseQueryBuilder_RoundTripsMediaFiltersFromUrl()
    {
        var preset = new LibraryBrowsePreset
        {
            RouteBase = "/read",
            Title = "Read",
            HeroVariant = BrowseHeroVariant.Read,
            Tabs =
            [
                new BrowseTabPreset
                {
                    Id = "books",
                    Label = "Books",
                    MediaType = "Books",
                    GroupingOptions =
                    [
                        new("all", "All Books", "all"),
                        new("series", "Series", "series"),
                    ],
                },
            ],
        };

        var state = BrowseQueryBuilder.Read(
            preset,
            "books",
            "http://localhost/read/books?genres=Fantasy%2CMystery&creator=Silas%20Northwood&status=in-progress&year=2024");

        Assert.Equal(["Fantasy", "Mystery"], state.Genres);
        Assert.Equal("Silas Northwood", state.Creator);
        Assert.Equal("in-progress", state.Status);
        Assert.Equal("2024", state.Year);
    }

    [Fact]
    public void LanePages_UsePersistentHeadersRailsRealRoutesAndNoLandingHeroes()
    {
        var read = ReadSource("src/MediaEngine.Web/Components/Pages/ReadPage.razor");
        var watch = ReadSource("src/MediaEngine.Web/Components/Pages/WatchPage.razor");
        var listen = ReadSource("src/MediaEngine.Web/Components/Pages/ListenPage.razor");
        var collections = ReadSource("src/MediaEngine.Web/Components/Collections/CollectionsPage.razor");
        var hub = ReadSource("src/MediaEngine.Web/Components/MediaHub/MediaHubPage.razor");
        var laneHeader = ReadSource("src/MediaEngine.Web/Components/MediaHub/MediaLaneHeader.razor");

        Assert.Contains("<MediaSectionShell", read, StringComparison.Ordinal);
        Assert.Contains("<MediaSectionShell", watch, StringComparison.Ordinal);
        Assert.Contains("<MediaLaneHeader Title=\"Read\"", read, StringComparison.Ordinal);
        Assert.Contains("<MediaLaneHeader Title=\"Watch\"", watch, StringComparison.Ordinal);
        Assert.Contains("<MediaLaneHeader Title=\"Listen\"", listen, StringComparison.Ordinal);
        Assert.Contains("<SurfaceNavigationBar", laneHeader, StringComparison.Ordinal);
        Assert.Contains("/watch/series", watch, StringComparison.Ordinal);
        Assert.Contains("SupportsHero=\"false\"", read, StringComparison.Ordinal);
        Assert.Contains("SupportsHero=\"false\"", watch, StringComparison.Ordinal);
        Assert.Contains("SupportsHero=\"false\"", listen, StringComparison.Ordinal);
        Assert.Contains("SupportsHeader=\"false\"", read, StringComparison.Ordinal);
        Assert.Contains("SupportsHeader=\"false\"", watch, StringComparison.Ordinal);
        Assert.Contains("SupportsHeader=\"false\"", listen, StringComparison.Ordinal);
        Assert.Contains("SupportsNavigation=\"false\"", read, StringComparison.Ordinal);
        Assert.Contains("SupportsNavigation=\"false\"", watch, StringComparison.Ordinal);
        Assert.Contains("SupportsNavigation=\"false\"", listen, StringComparison.Ordinal);
        Assert.Contains("ShowTitleBlock=\"false\"", read, StringComparison.Ordinal);
        Assert.Contains("ShowTitleBlock=\"false\"", watch, StringComparison.Ordinal);
        Assert.Contains("ShowTabNavigation=\"false\"", read, StringComparison.Ordinal);
        Assert.Contains("ShowTabNavigation=\"false\"", watch, StringComparison.Ordinal);
        Assert.DoesNotContain("<CinematicHeroCarousel", collections, StringComparison.Ordinal);
        Assert.Contains("Nav.NavigateTo(target.Route)", hub, StringComparison.Ordinal);
    }

    private static string ReadSource(string relativePath) =>
        File.ReadAllText(Path.Combine(FindRepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MediaEngine.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
