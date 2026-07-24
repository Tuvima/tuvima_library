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
            "http://localhost/read/books?genre=Fantasy&genre=Mystery&person=Silas%20Northwood&person=Ada%20Vale&status=in-progress&year=2024&year=2023");

        Assert.Equal(["Fantasy", "Mystery"], state.Genres);
        Assert.Equal(["Silas Northwood", "Ada Vale"], state.Creators);
        Assert.Equal("in-progress", state.Status);
        Assert.Equal(["2024", "2023"], state.Years);
    }

    [Fact]
    public void LanePages_UsePersistentHeadersRailsRealRoutesAndNoLandingHeroes()
    {
        var read = ReadSource("src/MediaEngine.Web/Components/Pages/ReadPage.razor");
        var watch = ReadSource("src/MediaEngine.Web/Components/Pages/WatchPage.razor");
        var listen = ReadSource("src/MediaEngine.Web/Components/Pages/ListenBrowsePage.razor");
        var listenPreset = ReadSource("src/MediaEngine.Web/Components/Pages/ListenBrowseConfiguration.cs");
        var collections = ReadSource("src/MediaEngine.Web/Components/Collections/CollectionsPage.razor");
        var hub = ReadSource("src/MediaEngine.Web/Components/MediaHub/MediaHubPage.razor");
        var laneHeader = ReadSource("src/MediaEngine.Web/Components/MediaHub/MediaLaneHeader.razor");
        var sectionShell = ReadSource("src/MediaEngine.Web/Components/MediaHub/MediaSectionShell.razor");
        var sectionShellStyles = ReadSource("src/MediaEngine.Web/Components/MediaHub/MediaSectionShell.razor.css");
        var browseShell = ReadSource("src/MediaEngine.Web/Components/Browse/MediaBrowseShell.razor");
        var browseShellStyles = ReadSource("src/MediaEngine.Web/Components/Browse/MediaBrowseShell.razor.css");
        var browseModeStyles = ReadSource("src/MediaEngine.Web/Components/Browse/AppBrowseModeSelector.razor.css");
        var multiSelect = ReadSource("src/MediaEngine.Web/Components/Browse/BrowseMultiSelect.razor");
        var multiSelectStyles = ReadSource("src/MediaEngine.Web/Components/Browse/BrowseMultiSelect.razor.css");
        var mediaShelf = ReadSource("src/MediaEngine.Web/Components/MediaHub/MediaShelf.razor");

        Assert.Contains("<MediaSectionShell", read, StringComparison.Ordinal);
        Assert.Contains("<MediaSectionShell", watch, StringComparison.Ordinal);
        Assert.Contains("<MediaLaneHeader Title=\"Read\"", read, StringComparison.Ordinal);
        Assert.Contains("<MediaLaneHeader Title=\"Watch\"", watch, StringComparison.Ordinal);
        Assert.Contains("<MediaLaneHeader Title=\"Listen\"", listen, StringComparison.Ordinal);
        Assert.Contains("<SurfaceNavigationBar", laneHeader, StringComparison.Ordinal);
        Assert.DoesNotContain("media-lane-header__identity", laneHeader, StringComparison.Ordinal);
        Assert.DoesNotContain("[Parameter] public string? Subtitle", laneHeader, StringComparison.Ordinal);
        Assert.DoesNotContain("media-section-shell__rail-title", sectionShell, StringComparison.Ordinal);
        Assert.Contains("Nav.LocationChanged += OnLocationChanged", sectionShell, StringComparison.Ordinal);
        Assert.Contains("Nav.LocationChanged -= OnLocationChanged", sectionShell, StringComparison.Ordinal);
        Assert.Contains("height: calc(100dvh - var(--app-topbar-height, 65px) - 1rem)", sectionShellStyles, StringComparison.Ordinal);
        Assert.Contains(".media-section-shell__content", sectionShellStyles, StringComparison.Ordinal);
        Assert.Contains("overflow-y: auto", sectionShellStyles, StringComparison.Ordinal);
        Assert.DoesNotContain("/watch/series", watch, StringComparison.Ordinal);
        Assert.Contains("new(\"series\", \"Movie Series\"", watch, StringComparison.Ordinal);
        Assert.DoesNotContain("new(\"collections\", \"Collections\"", watch, StringComparison.Ordinal);
        Assert.DoesNotContain("new(\"Movie Series\",", watch, StringComparison.Ordinal);
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
        Assert.Contains("GridHoverMode => MediaTileHoverMode.GlowOnly", browseShell, StringComparison.Ordinal);
        Assert.Contains("if (IsPersonGrouping)", browseShell, StringComparison.Ordinal);
        Assert.Contains("MediaPersonGroupTileComposer.Compose", browseShell, StringComparison.Ordinal);
        Assert.Contains("group.PersonId ?? group.ArtistPersonId", browseShell, StringComparison.Ordinal);
        Assert.Contains("MediaPersonGroupTileComposer.NavigationUrl", browseShell, StringComparison.Ordinal);
        Assert.Contains("group.DisplayName,\n            PersonDetailContext)", browseShell.Replace("\r\n", "\n"), StringComparison.Ordinal);
        Assert.Contains("(\"audiobooks\", \"series\") => $\"/details/bookseries/{group.CollectionId:D}?context=listen\"", browseShell, StringComparison.Ordinal);
        Assert.Contains("browse-shell__filter-surface", browseShell, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: max-content minmax(0, 1fr) max-content", browseShellStyles, StringComparison.Ordinal);
        Assert.Contains(".browse-shell__control-group--filters", browseShellStyles, StringComparison.Ordinal);
        Assert.Contains("border-left: 1px solid rgba(128, 146, 180, 0.2)", browseShellStyles, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 1680px)", browseShellStyles, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 1400px)", browseShellStyles, StringComparison.Ordinal);
        Assert.Contains("font-size: 0.88rem", browseShellStyles, StringComparison.Ordinal);
        Assert.Contains("<BrowseMultiSelect Label=\"Filters\"", browseShell, StringComparison.Ordinal);
        Assert.Contains("Search genres...", browseShell, StringComparison.Ordinal);
        Assert.Contains("Search years...", browseShell, StringComparison.Ordinal);
        Assert.Contains("SelectedValuesChanged=\"OnCreatorsChanged\"", browseShell, StringComparison.Ordinal);
        Assert.Contains("SelectedValuesChanged=\"OnYearsChanged\"", browseShell, StringComparison.Ordinal);
        Assert.Contains("SupportsFacetFilters => true", browseShell, StringComparison.Ordinal);
        Assert.Contains("<AppBrowseModeSelector", browseShell, StringComparison.Ordinal);
        Assert.Contains(".app-browse-mode__option:first-child", browseModeStyles, StringComparison.Ordinal);
        Assert.Contains("border-radius: 9px 0 0 9px", browseModeStyles, StringComparison.Ordinal);
        Assert.Contains(".app-browse-mode__option:last-child", browseModeStyles, StringComparison.Ordinal);
        Assert.Contains("border-radius: 0 9px 9px 0", browseModeStyles, StringComparison.Ordinal);
        Assert.Contains("<AppQuickFilterToggle", browseShell, StringComparison.Ordinal);
        Assert.Contains("<AppActiveFilterSummary", browseShell, StringComparison.Ordinal);
        Assert.Contains("<AppTimelineResults", browseShell, StringComparison.Ordinal);
        Assert.DoesNotContain("MediaBrowseNavigationBuilder.BuildBrowseGroup(Preset)", read, StringComparison.Ordinal);
        Assert.DoesNotContain("MediaBrowseNavigationBuilder.BuildBrowseGroup(Preset)", watch, StringComparison.Ordinal);
        Assert.Contains("new(\"My List\", \"/my-list\"", read, StringComparison.Ordinal);
        Assert.Contains("new(\"My List\", \"/my-list\"", watch, StringComparison.Ordinal);
        Assert.DoesNotContain("Label=\"Browse audiobooks\"", listen, StringComparison.Ordinal);
        Assert.Contains("browse-multi-select__option", multiSelect, StringComparison.Ordinal);
        Assert.Contains(".browse-multi-select ::deep .mud-menu-activator", multiSelectStyles, StringComparison.Ordinal);
        Assert.Contains("height: 48px", multiSelectStyles, StringComparison.Ordinal);
        Assert.Contains("ShowLabel=\"false\"", browseShell, StringComparison.Ordinal);
        Assert.DoesNotContain("OnClick=\"@context.ToggleAsync\"", multiSelect, StringComparison.Ordinal);
        Assert.DoesNotContain(".. IsSeriesOnly ? new[] { \"Series only\" }", browseShell, StringComparison.Ordinal);
        Assert.Contains("ShowCompactCaptions=\"true\"", browseShell, StringComparison.Ordinal);
        Assert.Contains("HideGroupIndicators=\"true\"", browseShell, StringComparison.Ordinal);
        Assert.Contains("browse-shell__tile-size", browseShell, StringComparison.Ordinal);
        Assert.Contains("TileSizePx", browseShell, StringComparison.Ordinal);
        Assert.Contains("SectionType == MediaHubSectionType.Listen", mediaShelf, StringComparison.Ordinal);
        Assert.Contains("ShowCompactCaptions=\"@(SectionType == MediaHubSectionType.Listen)\"", mediaShelf, StringComparison.Ordinal);
        Assert.Contains("MediaTileHoverMode.GlowOnly", mediaShelf, StringComparison.Ordinal);
        Assert.DoesNotContain("<CinematicHeroCarousel", collections, StringComparison.Ordinal);
        Assert.Contains("Nav.NavigateTo(target.Route)", hub, StringComparison.Ordinal);

        Assert.Contains("ListenBrowseConfiguration.Preset", listen, StringComparison.Ordinal);
        Assert.Contains("MediaBrowseNavigationBuilder.BuildBrowseGroup", listen, StringComparison.Ordinal);
        Assert.Contains("new(\"albums\", \"Albums\"", listenPreset, StringComparison.Ordinal);
        Assert.Contains("new(\"artists\", \"Artists\"", listenPreset, StringComparison.Ordinal);
        Assert.Contains("new(\"songs\", \"Songs\"", listenPreset, StringComparison.Ordinal);
        Assert.Contains("new(\"playlists\", \"Playlists\"", listenPreset, StringComparison.Ordinal);
        Assert.Contains("new(\"narrators\", \"Narrators\"", listenPreset, StringComparison.Ordinal);
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
