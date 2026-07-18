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
        Services.AddScoped(_ => new PlaybackSessionController(null!, api));
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
        Assert.Single(cut.FindAll("button[aria-label='Not for me']"));
        Assert.NotEmpty(cut.FindAll("button[aria-label='Like']"));
        Assert.NotEmpty(cut.FindAll("button[aria-label='Love']"));
        Assert.NotEmpty(cut.FindAll("button[aria-label='Add to My List']"));
        Assert.NotEmpty(cut.FindAll(".media-tile-reaction-menu button[aria-label='Rate this title']"));
        Assert.Equal(3, cut.FindAll(".media-tile-reaction-tray button").Count);
        Assert.Empty(cut.FindAll(".media-tile-visible-dislike-action"));
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
    public void MediaTileGrid_RoutesOnlyOptedInGroupsToDedicatedLandscapeComponent()
    {
        var group = CreateGroupTile();
        var ordinary = CreateShelfItem("Ordinary title");

        var cut = RenderComponent<MediaTileGrid>(parameters => parameters.Add(
            component => component.Items,
            [group, ordinary]));

        var renderedGroup = cut.FindComponent<MediaGroupTile>();
        var renderedOrdinary = cut.FindComponent<MediaTile>();

        Assert.Same(group, renderedGroup.Instance.Item);
        Assert.Same(ordinary, renderedOrdinary.Instance.Item);
        Assert.Single(cut.FindAll(".media-group-tile"));
        Assert.Single(cut.FindAll(".media-tile"));
    }

    [Fact]
    public void MediaGroupTile_ChangesSelectedItemWithoutChangingCardDimensions()
    {
        var group = CreateGroupTile();
        var cut = RenderComponent<MediaGroupTile>(parameters => parameters.Add(component => component.Item, group));
        var root = cut.Find("article.media-group-tile");

        Assert.Contains(root.Attributes, attribute => attribute.Name.StartsWith("b-", StringComparison.Ordinal));
        Assert.Contains("Foundation", cut.Find(".media-artwork-carousel__caption").TextContent);
        Assert.Equal("Book Series: Foundation Series", cut.Find(".media-group-tile__identity .media-group-tile__kind").TextContent.Trim());
        Assert.Equal("Foundation", cut.Find(".media-group-tile__identity h3").TextContent.Trim());
        Assert.Equal("The first Foundation novel.", cut.Find(".media-group-tile__description").TextContent.Trim());
        Assert.Contains("1951", cut.Find(".media-group-tile__facts").TextContent);
        Assert.Contains("4.4", cut.Find(".media-group-tile__rating").TextContent);
        Assert.DoesNotContain("2 books", cut.Find(".media-group-tile__facts").TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("owned titles", cut.Find(".media-group-tile__facts").TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Book 1", cut.Find(".media-artwork-carousel__caption").TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(cut.FindAll(".media-tile"));
        Assert.Contains("Open Series", cut.Find(".media-group-tile__group-action").TextContent);
        Assert.Contains("Open Book", cut.Find(".media-group-tile__item-action").TextContent);

        cut.Find("a.media-group-tile__base").Click();
        var nav = Services.GetRequiredService<NavigationManager>();
        Assert.EndsWith("/details/bookseries/foundation", nav.Uri, StringComparison.Ordinal);

        cut.Find("button[aria-label='Next book']").Click();

        Assert.Contains("Foundation and Empire", cut.Find(".media-artwork-carousel__caption").TextContent);
        Assert.Equal("Foundation and Empire", cut.Find(".media-group-tile__identity h3").TextContent.Trim());
        Assert.Equal("The second Foundation novel.", cut.Find(".media-group-tile__description").TextContent.Trim());
        Assert.Contains("1952", cut.Find(".media-group-tile__facts").TextContent);
        Assert.Contains("2 of 2", cut.Find(".media-artwork-carousel__pagination").TextContent);
        Assert.DoesNotContain("Book 2", cut.Find(".media-artwork-carousel__caption").TextContent, StringComparison.OrdinalIgnoreCase);
        cut.Find("button[aria-label='Open Foundation and Empire']").Click();

        Assert.EndsWith("/book/2?mode=read", nav.Uri, StringComparison.Ordinal);

        var css = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Web/Components/MediaTiles/MediaGroupTile.razor.css"));
        Assert.Contains("--media-group-tile-width: clamp(364px, 26.6vw, 504px)", css, StringComparison.Ordinal);
        Assert.Contains("--media-group-tile-height: clamp(280px, 17.5vw, 350px)", css, StringComparison.Ordinal);
        Assert.Contains("height: 100%", css, StringComparison.Ordinal);
        Assert.DoesNotContain(".media-group-tile:hover {\n    transform:", css.ReplaceLineEndings("\n"), StringComparison.Ordinal);
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

    private static MediaTileViewModel CreateGroupTile() => new()
    {
        Id = Guid.NewGuid(),
        CollectionId = Guid.NewGuid(),
        Title = "Foundation Series",
        MediaKind = "Book",
        Shape = MediaTileShape.Landscape,
        Presentation = MediaTilePresentation.BookSeries,
        SurfaceKind = MediaTileSurfaceKind.BannerLandscape,
        HoverMode = MediaTileHoverMode.Expanded,
        NavigationUrl = "/details/bookseries/foundation",
        PrimaryNavigationUrl = "/details/bookseries/foundation",
        PrimaryActionLabel = "Open Series",
        IsCollection = true,
        UseLandscapeGroupTile = true,
        PreviewTotalCount = 2,
        HoverFacts = ["2 owned titles"],
        ArtworkStackItems =
        [
            new ArtworkStackItem
            {
                Id = "1",
                WorkId = Guid.NewGuid(),
                Title = "Foundation",
                ImageUrl = "/covers/1.jpg",
                MediaType = "Book",
                NavigationUrl = "/book/1?mode=read",
                Shape = ArtworkShape.Portrait,
                Position = "1",
                Description = "The first Foundation novel.",
                Facts = ["Isaac Asimov", "1951", "★ 4.4"],
            },
            new ArtworkStackItem
            {
                Id = "2",
                WorkId = Guid.NewGuid(),
                Title = "Foundation and Empire",
                ImageUrl = "/covers/2.jpg",
                MediaType = "Book",
                NavigationUrl = "/book/2?mode=read",
                Shape = ArtworkShape.Portrait,
                Position = "2",
                Description = "The second Foundation novel.",
                Facts = ["Isaac Asimov", "1952", "★ 4.5"],
            },
        ],
    };

    [Fact]
    public void MediaTile_PortraitSeriesUsesFourCellCollageWithTwoItemsDiagonal()
    {
        var item = new MediaTileViewModel
        {
            Id = Guid.NewGuid(),
            CollectionId = Guid.NewGuid(),
            Title = "Foundation Series",
            MediaKind = "Book",
            Shape = MediaTileShape.Portrait,
            Presentation = MediaTilePresentation.BookSeries,
            SurfaceKind = MediaTileSurfaceKind.CoverPortrait,
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

        Assert.NotEmpty(cut.FindAll(".media-tile.is-portrait.is-ordered-series-card"));
        var restingCells = cut.Find(".media-tile-media").QuerySelectorAll(".media-tile-collage__cell");
        Assert.Equal(4, restingCells.Length);
        Assert.Single(restingCells[0].QuerySelectorAll("img"));
        Assert.Empty(restingCells[1].QuerySelectorAll("img"));
        Assert.Empty(restingCells[2].QuerySelectorAll("img"));
        Assert.Single(restingCells[3].QuerySelectorAll("img"));
        Assert.Equal(4, cut.Find(".media-tile-hover-art").QuerySelectorAll(".media-tile-collage__cell").Length);
        Assert.Empty(cut.FindAll(".media-tile-collection-copy"));
        Assert.Empty(cut.FindAll(".media-tile-caption"));
        Assert.Contains("Book Series", cut.Find(".media-tile-group-kind").TextContent);
        Assert.Empty(cut.FindAll(".media-tile-hover-series-stack"));
    }

    [Fact]
    public void MediaTileCollage_WithThreeItemsLeavesOnlyLastCellBlank()
    {
        var items = new List<ArtworkStackItem>
        {
            new() { Id = "1", Title = "One", ImageUrl = "/covers/1.jpg" },
            new() { Id = "2", Title = "Two", ImageUrl = "/covers/2.jpg" },
            new() { Id = "3", Title = "Three", ImageUrl = "/covers/3.jpg" },
        };

        var cut = RenderComponent<MediaTileCollage>(parameters => parameters
            .Add(component => component.Items, items)
            .Add(component => component.PreserveOrder, true));

        var cells = cut.FindAll(".media-tile-collage__cell");
        Assert.Equal(4, cells.Count);
        Assert.Single(cells[0].QuerySelectorAll("img"));
        Assert.Single(cells[1].QuerySelectorAll("img"));
        Assert.Single(cells[2].QuerySelectorAll("img"));
        Assert.Empty(cells[3].QuerySelectorAll("img"));
    }

    [Fact]
    public void MediaTile_TvShowDisplaysOwnedEpisodeCountWithoutEpisodeCollage()
    {
        var item = new MediaTileViewModel
        {
            Id = Guid.NewGuid(),
            Title = "Foundation",
            MediaKind = "TV",
            WorkId = Guid.NewGuid(),
            Shape = MediaTileShape.Portrait,
            Presentation = MediaTilePresentation.TvSeries,
            SurfaceKind = MediaTileSurfaceKind.CoverPortrait,
            HoverLayout = MediaTileHoverLayout.BannerPopover,
            HoverMode = MediaTileHoverMode.Expanded,
            TileImageUrl = "/shows/foundation.jpg",
            HoverImageUrl = "/shows/foundation-background.jpg",
            NavigationUrl = "/watch/tv/show/1",
            IsCollection = true,
            PreviewTotalCount = 12,
        };

        var cut = RenderComponent<MediaTile>(parameters => parameters.Add(component => component.Item, item));

        Assert.Contains("TV Show", cut.Find(".media-tile-group-kind").TextContent);
        Assert.Equal("12 episodes owned", cut.Find(".media-tile-group-count").GetAttribute("aria-label"));
        Assert.Empty(cut.FindAll(".media-tile-collage"));
        Assert.DoesNotContain("is-collection-card", cut.Find("article.media-tile").ClassList);
        Assert.DoesNotContain("is-collection-hover", cut.Find(".media-tile-hover-panel").ClassList);
        Assert.Contains("is-banner-popover", cut.Find(".media-tile-hover-panel").ClassList);
        Assert.Equal("/shows/foundation-background.jpg", cut.Find(".media-tile-hover-image").GetAttribute("src"));
        Assert.NotEmpty(cut.FindAll(".media-tile-hover-body"));
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

    [Theory]
    [InlineData("Book", "/read/")]
    [InlineData("Comic", "/read/")]
    [InlineData("Movie", "/watch/player/")]
    [InlineData("TV", "/watch/player/")]
    public void MediaTile_PrimaryActionLaunchesOwnedAssetPlayer(string mediaKind, string expectedRoutePrefix)
    {
        var assetId = Guid.NewGuid();
        var item = new MediaTileViewModel
        {
            Id = Guid.NewGuid(),
            WorkId = Guid.NewGuid(),
            AssetId = assetId,
            Title = "Playable title",
            MediaKind = mediaKind,
            Shape = mediaKind is "Movie" or "TV" ? MediaTileShape.Landscape : MediaTileShape.Portrait,
            SurfaceKind = mediaKind is "Movie" or "TV" ? MediaTileSurfaceKind.BannerLandscape : MediaTileSurfaceKind.CoverPortrait,
            HoverLayout = mediaKind is "Movie" or "TV" ? MediaTileHoverLayout.BannerPopover : MediaTileHoverLayout.ArtOnlyPopover,
            HoverMode = MediaTileHoverMode.Expanded,
            TileImageUrl = "/art/playable.jpg",
            HoverImageUrl = "/art/playable.jpg",
            NavigationUrl = "/details/work",
            DetailsNavigationUrl = "/details/work",
            PrimaryNavigationUrl = "/details/work",
            PrimaryActionLabel = mediaKind is "Book" or "Comic" ? "Read" : "Play",
        };

        var cut = RenderComponent<MediaTile>(parameters => parameters.Add(component => component.Item, item));

        cut.Find($"button[aria-label='{item.PrimaryActionLabel}']").Click();

        var nav = Services.GetRequiredService<NavigationManager>();
        Assert.EndsWith($"{expectedRoutePrefix}{assetId:D}", nav.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void MediaTile_MusicPrimaryActionStartsPersistentPlaybackWithoutNavigation()
    {
        var assetId = Guid.NewGuid();
        var workId = Guid.NewGuid();
        var item = new MediaTileViewModel
        {
            Id = workId,
            WorkId = workId,
            AssetId = assetId,
            Title = "Playable song",
            Subtitle = "Test Artist",
            MediaKind = "Music",
            Shape = MediaTileShape.Square,
            SurfaceKind = MediaTileSurfaceKind.CoverSquare,
            HoverLayout = MediaTileHoverLayout.ArtOnlyPopover,
            HoverMode = MediaTileHoverMode.Expanded,
            TileImageUrl = "/art/song.jpg",
            HoverImageUrl = "/art/song.jpg",
            NavigationUrl = "/listen/music/songs",
            DetailsNavigationUrl = "/details/musictrack",
            PrimaryNavigationUrl = "/listen/music/songs",
            PrimaryActionLabel = "Play",
        };
        var originalUri = Services.GetRequiredService<NavigationManager>().Uri;
        var cut = RenderComponent<MediaTile>(parameters => parameters.Add(component => component.Item, item));

        cut.Find("button[aria-label='Listen']").Click();

        var playback = Services.GetRequiredService<PlaybackSessionController>();
        Assert.Equal(originalUri, Services.GetRequiredService<NavigationManager>().Uri);
        Assert.Equal(workId, playback.CurrentItem?.WorkId);
        Assert.Equal(assetId, playback.CurrentItem?.AssetId);
    }

    [Fact]
    public void MediaTile_TvEpisodeKeepsSeasonEpisodeInResumeAction()
    {
        var details = "/watch/tv/show/1?episode=3";
        var item = new MediaTileViewModel
        {
            Id = Guid.NewGuid(),
            WorkId = Guid.NewGuid(),
            Title = "Episode Three",
            MediaKind = "TV",
            Shape = MediaTileShape.Landscape,
            SurfaceKind = MediaTileSurfaceKind.BannerLandscape,
            HoverLayout = MediaTileHoverLayout.BannerPopover,
            TileImageUrl = "/episodes/3.jpg",
            HoverImageUrl = "/episodes/3.jpg",
            NavigationUrl = details,
            DetailsNavigationUrl = details,
            PrimaryNavigationUrl = "/watch/player/3",
            PrimaryActionLabel = "Resume S1 E3",
            ProgressPct = 42,
        };

        var cut = RenderComponent<MediaTile>(parameters => parameters.Add(component => component.Item, item));

        Assert.NotEmpty(cut.FindAll("button[aria-label='Resume S1 E3']"));
        Assert.Contains("Resume S1 E3", cut.Find(".media-tile-hover-control-primary").TextContent);

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
        var tileSource = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Web/Components/MediaTiles/MediaTile.razor"));
        var appJs = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Web/wwwroot/app.js"));
        var layout = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Web/Shared/MainLayout.razor"));

        Assert.Contains("grid-template-columns: minmax(520px, 1.25fr) minmax(360px, .75fr)", css);
        Assert.Contains("--media-tile-media-height: clamp(400px, 25vw, 500px)", css);
        Assert.Contains(".media-tile-book-pages", css);
        Assert.Contains("background: transparent", css);
        Assert.Contains(".media-tile-feedback-control:focus-within", css);
        Assert.DoesNotContain("media-tile-visible-dislike-action", tileSource);
        Assert.Contains("media-tile-reaction-trigger", tileSource);
        Assert.Contains("media-tile-reaction-tray", tileSource);
        Assert.Contains("Icons.Material.Outlined.ThumbDownOffAlt", tileSource);
        Assert.Contains("background: color-mix(in srgb, var(--art-bg-base-dark, #080c12) 72%, transparent)", css);
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
        Assert.Contains("el.querySelectorAll('.media-tile, .media-group-tile')", appJs);
        Assert.Contains("tile.querySelector('.media-tile-frame, .media-group-tile__frame')", appJs);
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
