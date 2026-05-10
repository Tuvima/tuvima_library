using Bunit;
using MediaEngine.Contracts.Display;
using MediaEngine.Web.Components.Pages;
using MediaEngine.Web.Models.ViewDTOs;
using MediaEngine.Web.Services.Discovery;
using MediaEngine.Web.Services.Editing;
using MediaEngine.Web.Services.Integration;
using MediaEngine.Web.Services.Navigation;
using MediaEngine.Web.Tests.Support;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace MediaEngine.Web.Tests;

public sealed class Phase3BrowseFoundationTests : TestContext
{
    public Phase3BrowseFoundationTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLocalization();
        Services.AddLogging();
        Services.AddMudServices();
        Services.AddScoped<MediaEditorLauncherService>();
    }

    [Fact]
    public void Search_RendersResultsAndNoResultsStates()
    {
        Services.AddSingleton<IEngineApiClient>(EngineApiClientStub.Create(stub =>
        {
            stub.SetHandler(nameof(IEngineApiClient.SearchWorksAsync), args =>
            {
                var query = args?[0]?.ToString();
                return Task.FromResult(string.Equals(query, "dune", StringComparison.OrdinalIgnoreCase)
                    ? new List<SearchResultViewModel>
                    {
                        new()
                        {
                            WorkId = Guid.Parse("31000000-0000-0000-0000-000000000001"),
                            Title = "Dune",
                            Author = "Frank Herbert",
                            MediaType = "Book",
                            CollectionDisplayName = "Dune",
                        },
                    }
                    : []);
            });
        }));

        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo(navigation.GetUriWithQueryParameter("q", "dune"));

        var cut = RenderComponent<SearchPage>();
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Dune", cut.Markup);
            Assert.Contains("Frank Herbert", cut.Markup);
            Assert.Single(cut.FindAll(".search-result-row"));
        });

        navigation.NavigateTo(navigation.GetUriWithQueryParameter("q", "missing"));
        cut.WaitForAssertion(() => Assert.Contains("No results found", cut.Markup));
    }

    [Fact]
    public async Task Home_RendersRealShelvesWhenCatalogExists()
    {
        Services.AddSingleton<IEngineApiClient>(EngineApiClientStub.Create(stub =>
        {
            stub.SetHandler(nameof(IEngineApiClient.GetDisplayHomeAsync), _ => Task.FromResult<DisplayPageDto?>(CreateDisplayPage(
                "home",
                "Home",
                "recently-added",
                "Recently Added",
                "Book",
                "Project Hail Mary",
                "Andy Weir")));
        }));

        var composer = new DiscoveryComposerService(Services.GetRequiredService<IEngineApiClient>());

        var page = await composer.BuildHomeAsync();

        Assert.Equal("Project Hail Mary", page.Hero?.Title);
        Assert.Single(page.Shelves);
        Assert.Equal("Recently Added", page.Shelves[0].Title);
        Assert.Equal("Book", page.Catalog[0].MediaKind);
    }

    [Fact]
    public async Task Home_EmptyStateShowsSetupPathWhenNoCatalog()
    {
        Services.AddSingleton<IEngineApiClient>(EngineApiClientStub.Create(stub =>
        {
            stub.SetHandler(nameof(IEngineApiClient.GetDisplayHomeAsync), _ => Task.FromResult<DisplayPageDto?>(new DisplayPageDto(
                "home",
                "Home",
                null,
                null,
                [],
                [])));
        }));

        var composer = new DiscoveryComposerService(Services.GetRequiredService<IEngineApiClient>());

        var page = await composer.BuildHomeAsync();

        Assert.Null(page.Hero);
        Assert.Empty(page.Shelves);
        Assert.Empty(page.Catalog);
        Assert.Contains("first story", page.EmptyTitle, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Book", "/book/32000000-0000-0000-0000-000000000001?mode=read")]
    [InlineData("Movie", "/watch/movie/32000000-0000-0000-0000-000000000001")]
    [InlineData("TV", "/details/TvEpisode/32000000-0000-0000-0000-000000000001?context=Watch")]
    [InlineData("Music", "/listen/music/songs?track=32000000-0000-0000-0000-000000000001")]
    [InlineData("Audiobook", "/book/32000000-0000-0000-0000-000000000001?mode=listen")]
    public void SearchResults_RouteToMediaSpecificSurfaces(string mediaType, string expectedRoute)
    {
        var result = new SearchResultViewModel
        {
            WorkId = Guid.Parse("32000000-0000-0000-0000-000000000001"),
            Title = "Known item",
            MediaType = mediaType,
        };

        Assert.Equal(expectedRoute, MediaNavigation.ForSearchResult(result));
    }

    [Fact]
    public void Discovery_DoesNotRenderFakeRecommendationsOrProgressWithoutData()
    {
        var page = DiscoveryComposerService.FromDisplayPage(CreateDisplayPage(
            "home",
            "Home",
            "read",
            "Read",
            "Book",
            "A Real Book",
            "A Real Author",
            includeProgress: false));

        Assert.DoesNotContain(page.Shelves, shelf => shelf.Title.Contains("Recommended", StringComparison.OrdinalIgnoreCase));
        Assert.All(page.Catalog, card => Assert.Null(card.ProgressPct));
    }

    private static DisplayPageDto CreateDisplayPage(
        string key,
        string title,
        string shelfKey,
        string shelfTitle,
        string mediaType,
        string itemTitle,
        string subtitle,
        bool includeProgress = false)
    {
        var workId = Guid.Parse("33000000-0000-0000-0000-000000000001");
        var assetId = Guid.Parse("33000000-0000-0000-0000-000000000002");
        var details = new DisplayActionDto("openWork", "Details", WorkId: workId, WebUrl: "/book/33000000-0000-0000-0000-000000000001");
        var resume = new DisplayActionDto("readAsset", "Continue", WorkId: workId, AssetId: assetId, WebUrl: "/read/33000000-0000-0000-0000-000000000002");
        var artwork = new DisplayArtworkDto("/cover.jpg", null, null, null, null, 900, 1400, null, null, null, null, null, null, "#5DCAA5");
        var card = new DisplayCardDto(
            Id: workId,
            WorkId: workId,
            AssetId: assetId,
            CollectionId: null,
            MediaType: mediaType,
            GroupingType: "work",
            Title: itemTitle,
            Subtitle: subtitle,
            Facts: [subtitle],
            Artwork: artwork,
            PreferredShape: "portrait",
            Presentation: "book",
            TileTextMode: "caption",
            PreviewPlacement: "bottom",
            Progress: includeProgress ? new DisplayProgressDto(48, "48%", DateTimeOffset.UtcNow, resume) : null,
            Actions: [details],
            Flags: new DisplayCardFlagsDto(false, true, true, false, false),
            SortTimestamp: DateTimeOffset.UtcNow);

        return new DisplayPageDto(
            key,
            title,
            null,
            new DisplayHeroDto(itemTitle, subtitle, "From your library", artwork, includeProgress ? card.Progress : null, [includeProgress ? resume : details]),
            [new DisplayShelfDto(shelfKey, shelfTitle, null, [card], null)],
            [card]);
    }
}
