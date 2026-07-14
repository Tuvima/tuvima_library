using Bunit;
using MediaEngine.Web.Components.MediaTiles;
using MediaEngine.Web.Models.ViewDTOs;
using MediaEngine.Web.Services.Integration;
using MediaEngine.Web.Services.Playback;
using MediaEngine.Web.Tests.Support;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace MediaEngine.Web.Tests;

public sealed class MediaTileSurfaceRenderTests : TestContext
{
    private int _profileRequestCount;
    private int _managedCollectionRequestCount;
    private int _createCollectionRequestCount;

    public MediaTileSurfaceRenderTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var api = EngineApiClientStub.Create(stub =>
        {
            stub.SetHandler(nameof(IEngineApiClient.GetProfilesAsync), _ =>
            {
                Interlocked.Increment(ref _profileRequestCount);
                return Task.FromResult(new List<ProfileViewModel>
                {
                    new(
                        Guid.Parse("00000000-0000-0000-0000-000000000001"),
                        "Test User",
                        "#C9922E",
                        "Administrator",
                        DateTimeOffset.UtcNow),
                });
            });
            stub.SetHandler(nameof(IEngineApiClient.GetManagedCollectionsAsync), _ =>
            {
                Interlocked.Increment(ref _managedCollectionRequestCount);
                return Task.FromResult(new List<ManagedCollectionViewModel>());
            });
            stub.SetHandler(nameof(IEngineApiClient.CreateCollectionAsync), _ =>
            {
                Interlocked.Increment(ref _createCollectionRequestCount);
                return Task.FromResult(true);
            });
        });
        Services.AddSingleton(api);
        Services.AddScoped<ActiveProfileSessionService>();
        Services.AddScoped<MediaReactionService>();
        Services.AddScoped<FavoriteService>();
    }

    [Fact]
    public void MediaTile_RestingCardIsArtworkOnlyAndHoverIsCompactIdentity()
    {
        var item = new MediaTileViewModel
        {
            Id = Guid.NewGuid(),
            WorkId = Guid.NewGuid(),
            Title = "Leviathan Wakes",
            Subtitle = "The Expanse, Book 1",
            Description = "This belongs on the detail page only.",
            HoverFacts = ["James S. A. Corey", "M", "2011", "592 pages", "★ 4.3"],
            MediaKind = "Book",
            Shape = MediaTileShape.Portrait,
            SurfaceKind = MediaTileSurfaceKind.CoverPortrait,
            HoverLayout = MediaTileHoverLayout.ArtOnlyPopover,
            HoverMode = MediaTileHoverMode.Expanded,
            TileImageUrl = "/art/leviathan-wakes.jpg",
            HoverImageUrl = "/art/leviathan-wakes.jpg",
            NavigationUrl = "/book/1",
            DetailsNavigationUrl = "/book/1",
            PrimaryNavigationUrl = "/read/1",
            PrimaryActionLabel = "Read",
            ProgressPct = 42,
        };

        var cut = RenderComponent<MediaTile>(parameters => parameters.Add(component => component.Item, item));

        Assert.Equal("Leviathan Wakes", cut.Find(".media-tile").GetAttribute("aria-label"));
        Assert.NotEmpty(cut.FindAll(".media-tile-image"));
        Assert.Empty(cut.FindAll(".media-tile-caption"));
        Assert.Empty(cut.FindAll(".media-tile-badge"));
        Assert.Empty(cut.FindAll(".media-tile-quality-badge"));
        Assert.Empty(cut.FindAll(".media-tile-source-badge"));
        Assert.Empty(cut.FindAll(".media-tile-progress-strip"));
        Assert.Empty(cut.FindAll(".media-tile-logo"));
        Assert.NotEmpty(cut.FindAll("div[style*='display: contents']"));
        Assert.Contains("--media-tile-hover-image", cut.Markup);
        Assert.NotEmpty(cut.FindAll(".media-tile-hover-cover-stage.is-book-stage"));
        Assert.NotEmpty(cut.FindAll(".media-tile-book-pages"));
        Assert.NotEmpty(cut.FindAll(".media-tile-book-spine"));
        Assert.Contains("Leviathan Wakes", cut.Find(".media-tile-hover-title").TextContent);
        Assert.Contains("The Expanse, Book 1", cut.Find(".media-tile-hover-subtitle").TextContent);
        Assert.Contains("592 pages", cut.Markup);
        Assert.Contains("4.3", cut.Find(".media-tile-rating-pill").TextContent);
        Assert.DoesNotContain(item.Description, cut.Markup);
        Assert.NotEmpty(cut.FindAll("button[aria-label='Not for me']"));
        Assert.NotEmpty(cut.FindAll("button[aria-label='Like']"));
        Assert.NotEmpty(cut.FindAll("button[aria-label='Love']"));
        Assert.NotEmpty(cut.FindAll("button[aria-label='Add to My List']"));
        Assert.NotEmpty(cut.FindAll(".media-tile-reaction-menu button[aria-label='Rate this title']"));
        Assert.Empty(cut.FindAll("button[aria-label='Add to collection']"));
        Assert.NotEmpty(cut.FindAll(".tl-media-action--compact"));
        Assert.NotEmpty(cut.FindAll(".media-tile-hover-progress"));

        cut.Find(".media-tile").TriggerEvent("onkeydown", new KeyboardEventArgs { Key = "ArrowDown" });
        Assert.Contains("is-expanded", cut.Find(".media-tile").ClassList);
    }

    [Fact]
    public void MediaTile_DetailsSurfaceIsSemanticLinkWithVisibleKeyboardFocus()
    {
        const string details = "/book/semantic-card";
        var item = new MediaTileViewModel
        {
            Id = Guid.NewGuid(),
            Title = "Semantic Card",
            MediaKind = "Book",
            Shape = MediaTileShape.Portrait,
            SurfaceKind = MediaTileSurfaceKind.CoverPortrait,
            HoverLayout = MediaTileHoverLayout.ArtOnlyPopover,
            TileImageUrl = "/art/semantic-card.jpg",
            NavigationUrl = details,
            DetailsNavigationUrl = details,
        };
        var cut = RenderComponent<MediaTile>(parameters => parameters.Add(component => component.Item, item));

        var card = cut.Find("article.media-tile");
        var detailsLink = cut.Find("a.media-tile-media");

        Assert.Null(card.GetAttribute("tabindex"));
        Assert.Equal(details, detailsLink.GetAttribute("href"));
        Assert.Equal("View details for Semantic Card", detailsLink.GetAttribute("aria-label"));

        detailsLink.Click();

        var nav = Services.GetRequiredService<NavigationManager>();
        Assert.EndsWith(details, nav.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void MediaTileShelf_PreservesCardInstancesWhenItemsReorder()
    {
        var first = CreateShelfItem("First");
        var second = CreateShelfItem("Second");
        var cut = RenderComponent<MediaTileShelf>(parameters => parameters.Add(
            component => component.Shelf,
            new MediaTileShelfViewModel
            {
                Key = "reorder-test",
                Title = "Reorder test",
                Items = [first, second],
            }));
        var initialInstances = cut.FindComponents<MediaTile>()
            .ToDictionary(component => component.Instance.Item.Id, component => component.Instance);

        cut.SetParametersAndRender(parameters => parameters.Add(
            component => component.Shelf,
            new MediaTileShelfViewModel
            {
                Key = "reorder-test",
                Title = "Reorder test",
                Items = [second, first],
            }));
        var reorderedInstances = cut.FindComponents<MediaTile>()
            .ToDictionary(component => component.Instance.Item.Id, component => component.Instance);

        Assert.Same(initialInstances[first.Id], reorderedInstances[first.Id]);
        Assert.Same(initialInstances[second.Id], reorderedInstances[second.Id]);
    }

    [Fact]
    public void MediaTileShelf_ManyCardsShareProfileAndReadStateWithoutCreatingCollections()
    {
        var items = Enumerable.Range(1, 24)
            .Select(index => new MediaTileViewModel
            {
                Id = Guid.NewGuid(),
                WorkId = Guid.NewGuid(),
                Title = $"Card {index}",
                NavigationUrl = $"/details/{index}",
                HoverMode = MediaTileHoverMode.Expanded,
            })
            .ToList();

        RenderComponent<MediaTileShelf>(parameters => parameters.Add(
            component => component.Shelf,
            new MediaTileShelfViewModel
            {
                Key = "request-count-test",
                Title = "Request count test",
                Items = items,
            }));

        Assert.Equal(1, _profileRequestCount);
        Assert.Equal(2, _managedCollectionRequestCount);
        Assert.Equal(0, _createCollectionRequestCount);
    }

    [Fact]
    public void MediaTile_EnrichedLogoReplacesHoverTitleButNotRestingArtwork()
    {
        var item = new MediaTileViewModel
        {
            Id = Guid.NewGuid(),
            WorkId = Guid.NewGuid(),
            Title = "Arrival",
            HoverFacts = ["PG-13", "2016", "1h 56m", "★ 7.9"],
            MediaKind = "Movie",
            Shape = MediaTileShape.Portrait,
            SurfaceKind = MediaTileSurfaceKind.CoverPortrait,
            HoverLayout = MediaTileHoverLayout.BannerPopover,
            HoverMode = MediaTileHoverMode.Expanded,
            TileImageUrl = "/art/arrival-poster.jpg",
            HoverImageUrl = "/art/arrival-background.jpg",
            LogoUrl = "/art/arrival-logo.png",
            NavigationUrl = "/watch/movie/1",
            PrimaryNavigationUrl = "/watch/player/1",
            PrimaryActionLabel = "Play",
        };

        var cut = RenderComponent<MediaTile>(parameters => parameters.Add(component => component.Item, item));

        Assert.Empty(cut.FindAll(".media-tile-logo"));
        Assert.NotEmpty(cut.FindAll(".media-tile-hover-logo"));
        Assert.Empty(cut.FindAll(".media-tile-hover-title"));
        Assert.Contains("PG-13", cut.Markup);
        Assert.Contains("1h 56m", cut.Markup);
    }

    private static MediaTileViewModel CreateShelfItem(string title) => new()
    {
        Id = Guid.NewGuid(),
        Title = title,
        NavigationUrl = $"/details/{title.ToLowerInvariant()}",
        HoverMode = MediaTileHoverMode.None,
    };

    [Fact]
    public void MediaTile_LandscapeSeriesKeepsOrderedArtworkWithoutRestingCopy()
    {
        var item = new MediaTileViewModel
        {
            Id = Guid.NewGuid(),
            CollectionId = Guid.NewGuid(),
            Title = "Foundation Series",
            MediaKind = "Book",
            Shape = MediaTileShape.Landscape,
            Presentation = MediaTilePresentation.BookSeries,
            SurfaceKind = MediaTileSurfaceKind.BannerLandscape,
            HoverLayout = MediaTileHoverLayout.BannerPopover,
            HoverMode = MediaTileHoverMode.Expanded,
            TileImageUrl = "/art/foundation-bg.jpg",
            HoverImageUrl = "/art/foundation-bg.jpg",
            NavigationUrl = "/details/bookseries/foundation",
            PrimaryNavigationUrl = "/details/bookseries/foundation",
            PrimaryActionLabel = "Open Series",
            IsCollection = true,
            ArtworkStackItems =
            [
                new ArtworkStackItem { Id = "1", Title = "Foundation", ImageUrl = "/covers/1.jpg", MediaType = "Book", Shape = ArtworkShape.Portrait, Position = "1" },
                new ArtworkStackItem { Id = "2", Title = "Foundation and Empire", ImageUrl = "/covers/2.jpg", MediaType = "Book", Shape = ArtworkShape.Portrait, Position = "2" },
            ],
        };

        var cut = RenderComponent<MediaTile>(parameters => parameters.Add(component => component.Item, item));

        Assert.NotEmpty(cut.FindAll(".media-tile.is-landscape.is-ordered-series-card"));
        Assert.Equal(4, cut.FindAll(".media-tile-collage__cell").Count);
        Assert.Empty(cut.FindAll(".media-tile-collection-copy"));
        Assert.Empty(cut.FindAll(".media-tile-caption"));
        Assert.Contains("Book Series", cut.Find(".media-tile-group-kind").TextContent);
        Assert.Empty(cut.FindAll(".media-tile-hover-series-stack"));
    }

    [Fact]
    public void MediaTile_TvShowDisplaysOwnedEpisodeCountWithoutEpisodeCollage()
    {
        var item = new MediaTileViewModel
        {
            Id = Guid.NewGuid(),
            Title = "Foundation",
            MediaKind = "TV",
            Shape = MediaTileShape.Portrait,
            Presentation = MediaTilePresentation.TvSeries,
            SurfaceKind = MediaTileSurfaceKind.CoverPortrait,
            HoverLayout = MediaTileHoverLayout.ArtOnlyPopover,
            TileImageUrl = "/shows/foundation.jpg",
            HoverImageUrl = "/shows/foundation.jpg",
            NavigationUrl = "/watch/tv/show/1",
            IsCollection = true,
            PreviewTotalCount = 12,
        };

        var cut = RenderComponent<MediaTile>(parameters => parameters.Add(component => component.Item, item));

        Assert.Contains("TV Show", cut.Find(".media-tile-group-kind").TextContent);
        Assert.Equal("12 episodes owned", cut.Find(".media-tile-group-count").GetAttribute("aria-label"));
        Assert.Empty(cut.FindAll(".media-tile-collage"));
    }

    [Fact]
    public void MediaTile_PrimaryCallbackAndDetailsNavigationRemainSeparate()
    {
        var details = "/book/1";
        var item = new MediaTileViewModel
        {
            Id = Guid.NewGuid(),
            WorkId = Guid.NewGuid(),
            Title = "Project Hail Mary",
            MediaKind = "Book",
            Shape = MediaTileShape.Portrait,
            SurfaceKind = MediaTileSurfaceKind.CoverPortrait,
            HoverLayout = MediaTileHoverLayout.ArtOnlyPopover,
            TileImageUrl = "/art/hail-mary.jpg",
            HoverImageUrl = "/art/hail-mary.jpg",
            NavigationUrl = details,
            DetailsNavigationUrl = details,
            PrimaryNavigationUrl = "/read/1",
            PrimaryActionLabel = "Read",
        };
        MediaTileViewModel? selected = null;
        var cut = RenderComponent<MediaTile>(parameters => parameters
            .Add(component => component.Item, item)
            .Add(component => component.OnPrimaryClicked, EventCallback.Factory.Create<MediaTileViewModel>(this, value => selected = value)));

        cut.Find("button[aria-label='Read']").Click();
        Assert.Same(item, selected);

        cut.Find(".media-tile-media").Click();
        var nav = Services.GetRequiredService<NavigationManager>();
        Assert.EndsWith(details, nav.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void MediaTile_AudioPrimaryActionUsesUnifiedListenLabel()
    {
        var item = new MediaTileViewModel
        {
            Id = Guid.NewGuid(),
            WorkId = Guid.NewGuid(),
            Title = "Long Audiobook",
            MediaKind = "Audiobook",
            Shape = MediaTileShape.Square,
            SurfaceKind = MediaTileSurfaceKind.CoverSquare,
            HoverLayout = MediaTileHoverLayout.ArtOnlyPopover,
            HoverMode = MediaTileHoverMode.Expanded,
            TileImageUrl = "/art/audio.jpg",
            HoverImageUrl = "/art/audio.jpg",
            NavigationUrl = "/listen/audiobook/1",
            PrimaryNavigationUrl = "/listen/audiobook/1",
            PrimaryActionLabel = "Play",
        };

        var cut = RenderComponent<MediaTile>(parameters => parameters.Add(component => component.Item, item));

        Assert.NotEmpty(cut.FindAll("button[aria-label='Listen']"));
        Assert.NotEmpty(cut.FindAll("button[aria-label='Add to My List']"));
        Assert.NotEmpty(cut.FindAll("button[aria-label='Not for me']"));
        Assert.NotEmpty(cut.FindAll("button[aria-label='Like']"));
        Assert.NotEmpty(cut.FindAll("button[aria-label='Love']"));
        Assert.Empty(cut.FindAll("button[aria-label='Add to collection']"));
    }

    [Fact]
    public void MediaTile_CssAndJavascriptProvideWideFallbackAndInRowExpansion()
    {
        var css = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Web/Components/MediaTiles/MediaTile.razor.css"));
        var appJs = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Web/wwwroot/app.js"));
        var layout = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Web/Shared/MainLayout.razor"));

        Assert.Contains("grid-template-columns: minmax(520px, 1.25fr) minmax(360px, .75fr)", css);
        Assert.Contains("--media-tile-media-height: clamp(400px, 25vw, 500px)", css);
        Assert.Contains(".media-tile-book-pages", css);
        Assert.Contains("background: transparent", css);
        Assert.Contains(".media-tile-feedback-control:focus-within", css);
        Assert.Contains("width: 188px", css);
        Assert.Contains("--media-tile-hover-anchor-height", css);
        Assert.Contains("clamp(820px, 52vw, 980px)", css);
        Assert.Contains("object-fit: contain", css);
        Assert.Contains("var(--art-bg-base-dark, #080c12)", css);
        Assert.Contains("filter: blur(42px) saturate(1.18) brightness(.72)", css);
        Assert.Contains("width: fit-content", css);
        Assert.Contains(".media-tile-hover-panel.is-inline-expanded", css);
        Assert.Contains("width .36s cubic-bezier(.2, .8, .2, 1)", css);
        Assert.Contains("grid-template-columns: minmax(0, 1.15fr) minmax(180px, .85fr)", css);
        Assert.Contains("-webkit-line-clamp: 2", css);
        Assert.Contains("font-size: clamp(1.2rem, 1.45vw, 1.6rem)", css);
        Assert.Contains("height: 44px", css);
        Assert.Contains("background: linear-gradient(to top, rgba(4, 7, 12, .38)", css);
        Assert.Contains("grid-template-columns: minmax(0, auto) 18px", css);
        Assert.Contains("max-height: calc(var(--media-tile-media-height, 400px) - 44px)", css);
        Assert.Contains("margin-block: 2%", css);
        Assert.Contains("window.updateMediaTileShelfStableHeight", appJs);
        Assert.Contains("window.getSwimlaneItems", appJs);
        Assert.Contains("el.querySelectorAll('.media-tile')", appJs);
        Assert.Contains("window.getSwimlaneItems(el).forEach", appJs);
        Assert.Contains("childRect.left - containerRect.left - paddingLeft", appJs);
        Assert.Contains("--media-tile-row-height", appJs);
        Assert.Contains("panel.classList.add('is-inline-expanded')", appJs);
        Assert.Contains("cardEl.style.setProperty('--media-tile-hover-anchor-width'", appJs);
        Assert.Contains("cardEl.style.setProperty('--media-tile-hover-anchor-height'", appJs);
        Assert.Contains("cardEl.style.setProperty('--media-tile-expanded-width'", appJs);
        Assert.Contains("window.keepMediaTileHoverInRowViewport", appJs);
        Assert.Contains("row.scrollTo({ left: row.scrollLeft + delta, behavior: 'smooth' })", appJs);
        Assert.DoesNotContain("}, 40);", appJs);
        Assert.Contains("}, 370);", appJs);
        Assert.Contains("var showDelay = activeCard && activeCard !== cardEl ? 45 : 240;", appJs);
        Assert.Contains("cardEl.closest('.media-tile-shelf-scroll, .media-tile-grid')", appJs);
        Assert.Contains("previousCard && previousCard !== cardEl", appJs);
        Assert.Contains("}, 110);", appJs);
        Assert.Contains("window.mountMediaTileHover(cardEl);", appJs);
        Assert.Contains("cardEl.closest('.media-tile-grid')", appJs);
        Assert.Contains("panel.classList.add('is-grid-overlay')", appJs);
        Assert.Contains("window.restoreMediaTileHover(cardEl);", appJs);
        Assert.Contains("window.registerMediaTileCollages", appJs);
        Assert.Contains("prefers-reduced-motion: reduce", appJs);
        Assert.Contains(".media-tile.is-square", css);
        Assert.Contains("--media-tile-media-height: clamp(400px, 25vw, 500px)", css);
        Assert.DoesNotContain("window.lockMediaTileHoverRowScroll(cardEl);", appJs);
        Assert.Contains(".media-tile-hover-panel.is-grid-overlay.is-inline-expanded", css);
        Assert.Contains("position: fixed !important", css);
        Assert.DoesNotContain("media-tile-hover-host", layout);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MediaEngine.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
