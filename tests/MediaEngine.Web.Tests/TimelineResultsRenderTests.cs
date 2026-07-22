using Bunit;
using MediaEngine.Web.Components.Browse;
using MediaEngine.Web.Models.ViewDTOs;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace MediaEngine.Web.Tests;

public sealed class TimelineResultsRenderTests : TestContext
{
    public TimelineResultsRenderTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
    }

    [Fact]
    public void Timeline_RendersVerticalYearRowsAndWorkingQuickNavigation()
    {
        var items = new List<MediaTileViewModel>
        {
            CreateItem("Starlight Dawn", 1977, MediaTileShape.Portrait),
            CreateItem("Void Creature", 1980, MediaTileShape.Square),
            CreateItem("Empire of Ash", 1980, MediaTileShape.Portrait),
            CreateItem("Unplaced Story", 0, MediaTileShape.Landscape),
        };

        var cut = RenderComponent<AppTimelineResults>(parameters => parameters
            .Add(component => component.Items, items)
            .Add(component => component.YearSemantic, "Original release year"));

        Assert.Equal(3, cut.FindAll(".app-timeline__year").Count);
        Assert.Empty(cut.FindAll(".app-timeline__grouping"));
        Assert.Contains("Original release year", cut.Find(".app-timeline__header").TextContent, StringComparison.Ordinal);
        Assert.Equal("1977", cut.Find(".app-timeline__year.is-selected h3").TextContent.Trim());
        Assert.Equal("location", cut.Find("a[href='/#timeline-year-1977']").GetAttribute("aria-current"));
        Assert.Single(cut.FindAll(".app-timeline__year-link.is-disabled"), link => link.TextContent.Trim() == "1978");
        Assert.Equal("2 items", cut.Find("#timeline-year-1980 .app-timeline__year-heading span").TextContent.Trim());
        Assert.Equal(2, cut.FindAll("#timeline-year-1980 .app-timeline__item").Count);
        Assert.Single(cut.FindAll(".app-timeline__artwork.is-square"));
        Assert.Single(cut.FindAll(".app-timeline__artwork.is-landscape"));
        Assert.Contains("app-timeline--art-92", cut.Find(".app-timeline").ClassList);

        cut.Find("a[href='/#timeline-year-1980']").Click();

        Assert.Equal("1980", cut.Find(".app-timeline__year.is-selected h3").TextContent.Trim());
        Assert.Equal("location", cut.Find("a[href='/#timeline-year-1980']").GetAttribute("aria-current"));
        Assert.Null(cut.Find("a[href='/#timeline-year-1977']").GetAttribute("aria-current"));
    }

    [Fact]
    public void Timeline_UsesVerticalLayoutWithoutTheRetiredHorizontalRail()
    {
        var source = ReadSource("src/MediaEngine.Web/Components/Browse/AppTimelineResults.razor");
        var styles = ReadSource("src/MediaEngine.Web/Components/Browse/AppTimelineResults.razor.css");

        Assert.Contains("Timeline quick navigation", source, StringComparison.Ordinal);
        Assert.Contains("href=\"@YearNavigationUrl(year)\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Group by", source, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: clamp(142px, 12vw, 184px) minmax(0, 1fr)", styles, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: minmax(132px, 170px) minmax(0, 1fr)", styles, StringComparison.Ordinal);
        Assert.DoesNotContain("grid-auto-flow: column", styles, StringComparison.Ordinal);
        Assert.DoesNotContain("scroll-snap-type: x", styles, StringComparison.Ordinal);
    }

    private static MediaTileViewModel CreateItem(string title, int year, MediaTileShape shape) => new()
    {
        Id = Guid.NewGuid(),
        WorkId = Guid.NewGuid(),
        Title = title,
        SortYear = year,
        Shape = shape,
        TileImageUrl = $"/artwork/{Uri.EscapeDataString(title)}.jpg",
        NavigationUrl = $"/details/{Uri.EscapeDataString(title)}",
    };

    private static string ReadSource(string relativePath) =>
        File.ReadAllText(Path.Combine(FindRepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MediaEngine.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
