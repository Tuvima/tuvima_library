using Bunit;
using MediaEngine.Contracts.Ingestion;
using MediaEngine.Web.Components.Settings;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace MediaEngine.Web.Tests;

public sealed class IngestionMediaCardTests : AsyncBunitContext
{
    public IngestionMediaCardTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
        Render<MudPopoverProvider>();
    }

    [Fact]
    public void ActiveGroup_SeparatesWorkFromIdentityAndDistinguishesFilesFromTracks()
    {
        var item = new IngestionMediaGroupDto
        {
            Title = "A Night at the Opera",
            Subtitle = "Queen",
            MediaType = "Music",
            Availability = "finishing",
            StatusLabel = "Adding lyrics",
            ChildUnit = "tracks",
            ChildCompleted = 5,
            FileCount = 6,
            Artwork = new() { State = "complete", Label = "Artwork complete" },
            TextTracks = new() { State = "active", Label = "Lyrics active" },
        };
        IngestionMediaGroupDto? opened = null;
        var cut = Render<IngestionMediaCard>(parameters => parameters
            .Add(component => component.Item, item)
            .Add(component => component.OnOpen, value => opened = value));

        Assert.Equal("Adding lyrics", cut.Find(".ingestion-media-card__activity").TextContent.Trim());
        Assert.Equal(item.Title, cut.Find(".ingestion-media-card__title").TextContent);
        Assert.Equal("Queen", cut.Find(".ingestion-media-card__contributor").TextContent);
        Assert.Equal("Album", cut.Find(".ingestion-media-card__type").TextContent.Trim());
        Assert.Equal("5 tracks added · 6 files", cut.Find(".ingestion-media-card__count").TextContent);
        Assert.DoesNotContain("Ready to browse", cut.Markup);
        Assert.Single(cut.FindAll(".ingestion-facet__spinner"));
        Assert.Equal(2, cut.FindAll(".ingestion-facets__slot").Count);
        Assert.Equal(2, cut.FindAll(".ingestion-facet").Count);
        cut.Find("button.ingestion-media-card").Click();
        Assert.Same(item, opened);
    }

    [Theory]
    [InlineData("ready", "Ready", "Ready to browse")]
    [InlineData("review", "Needs review", "Needs review")]
    [InlineData("failed", "Failed", "Failed")]
    public void MissingContributor_PreservesTheRowAndDoesNotInventMetadata(string availability, string status, string expected)
    {
        var cut = Render<IngestionMediaCard>(parameters => parameters.Add(component => component.Item, new()
        {
            Title = "The Quiet Mind",
            Subtitle = "Books",
            MediaType = "Books",
            Availability = availability,
            StatusLabel = status,
            FileCount = 1,
        }));

        Assert.Equal(expected, cut.Find(".ingestion-media-card__activity").TextContent.Trim());
        Assert.Empty(cut.Find(".ingestion-media-card__contributor").TextContent);
        Assert.Equal("Book", cut.Find(".ingestion-media-card__type").TextContent.Trim());
        Assert.Equal("1 file", cut.Find(".ingestion-media-card__count").TextContent);
        Assert.Single(cut.FindAll(".ingestion-facets"));
        Assert.Empty(cut.FindAll(".ingestion-facet"));
    }

    [Fact]
    public void DiscoveredFiles_AreNotReportedAsAddedBeforeTheyFinish()
    {
        var cut = Render<IngestionMediaCard>(parameters => parameters.Add(component => component.Item, new()
        {
            Title = "New album",
            MediaType = "Music",
            StatusLabel = "Identifying",
            ChildUnit = "tracks",
            FileCount = 8,
            ChildCompleted = 0,
        }));

        Assert.Equal("8 files", cut.Find(".ingestion-media-card__count").TextContent);
        Assert.DoesNotContain("8 tracks added", cut.Markup);
    }

    [Fact]
    public void ListMode_UsesCompactColumnsAndReadyStateIsOnlyACheckmark()
    {
        var cut = Render<IngestionMediaCard>(parameters => parameters
            .Add(component => component.DisplayMode, "list")
            .Add(component => component.Item, new()
            {
                Title = "The Quiet Mind",
                Subtitle = "Daniel Brooks",
                MediaType = "Audiobooks",
                Availability = "ready",
                StatusLabel = "Ready",
                ChildUnit = "files",
                ChildCompleted = 12,
                FileCount = 12,
                Artwork = new() { State = "complete", Label = "Artwork complete" },
                Metadata = new() { State = "complete", Label = "Metadata complete" },
            }));

        Assert.Single(cut.FindAll("button.ingestion-media-list-row"));
        Assert.Equal("The Quiet Mind", cut.Find(".ingestion-media-list-row__copy strong").TextContent);
        Assert.Equal("Daniel Brooks", cut.Find(".ingestion-media-list-row__copy small").TextContent);
        Assert.Contains("Audiobook", cut.Find(".ingestion-media-list-row__format").TextContent);
        Assert.Contains("12 files added", cut.Find(".ingestion-media-list-row__format").TextContent);
        Assert.Empty(cut.FindAll(".ingestion-media-list-row__status span"));
        Assert.Empty(cut.Find(".ingestion-media-list-row__status").TextContent.Trim());
    }
}
