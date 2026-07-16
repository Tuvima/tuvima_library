using Bunit;
using MediaEngine.Web.Components.MediaTiles;
using MediaEngine.Web.Components.Shared;
using MediaEngine.Web.Models.ViewDTOs;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace MediaEngine.Web.Tests;

public sealed class MediaArtworkCarouselTests : TestContext
{
    public MediaArtworkCarouselTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
    }

    [Fact]
    public void TwoItems_BothArrowsSelectTheOtherItem()
    {
        var items = CreateItems(2, ArtworkShape.Portrait);
        ArtworkStackItem? selected = null;
        var cut = RenderCarousel(items, items[0].Id, item => selected = item);

        cut.Find("button[aria-label='Previous book']").Click();
        Assert.Same(items[1], selected);

        selected = null;
        cut.Find("button[aria-label='Next book']").Click();
        Assert.Same(items[1], selected);
        Assert.Single(cut.FindAll(".media-artwork-carousel__side-artwork"));
    }

    [Fact]
    public void CompleteCarousel_WrapsFromBothEnds()
    {
        var items = CreateItems(3, ArtworkShape.Portrait);
        ArtworkStackItem? selected = null;
        var cut = RenderCarousel(items, items[^1].Id, item => selected = item);

        cut.Find("button[aria-label='Next book']").Click();
        Assert.Same(items[0], selected);

        cut.SetParametersAndRender(parameters => parameters
            .Add(component => component.Items, items)
            .Add(component => component.SelectedItemId, items[0].Id)
            .Add(component => component.SelectedItemChanged, EventCallback.Factory.Create<ArtworkStackItem>(this, (ArtworkStackItem item) => selected = item))
            .Add(component => component.ItemNoun, "book")
            .Add(component => component.Circular, true));
        cut.Find("button[aria-label='Previous book']").Click();
        Assert.Same(items[^1], selected);
    }

    [Fact]
    public void KeyboardNavigation_SupportsArrowsHomeAndEnd()
    {
        var items = CreateItems(4, ArtworkShape.Portrait);
        ArtworkStackItem? selected = null;
        var cut = RenderCarousel(items, items[1].Id, item => selected = item);
        var root = cut.Find(".media-artwork-carousel");

        root.TriggerEvent("onkeydown", new KeyboardEventArgs { Key = "ArrowRight" });
        Assert.Same(items[2], selected);

        root.TriggerEvent("onkeydown", new KeyboardEventArgs { Key = "ArrowLeft" });
        Assert.Same(items[0], selected);

        root.TriggerEvent("onkeydown", new KeyboardEventArgs { Key = "Home" });
        Assert.Same(items[0], selected);

        root.TriggerEvent("onkeydown", new KeyboardEventArgs { Key = "End" });
        Assert.Same(items[^1], selected);
    }

    [Fact]
    public void Carousel_BoundsIndicatorsAndHidesControlsForOneItem()
    {
        var many = CreateItems(9, ArtworkShape.Square);
        var cut = RenderCarousel(many, many[6].Id, _ => { });

        Assert.Equal(5, cut.FindAll(".media-artwork-carousel__dot").Count);
        Assert.Contains("7 of 9", cut.Find(".media-artwork-carousel__pagination").TextContent);

        cut.SetParametersAndRender(parameters => parameters
            .Add(component => component.Items, new[] { many[0] })
            .Add(component => component.SelectedItemId, many[0].Id)
            .Add(component => component.ItemNoun, "track"));

        Assert.Empty(cut.FindAll(".media-artwork-carousel__arrow"));
        Assert.Empty(cut.FindAll(".media-artwork-carousel__pagination"));
    }

    [Fact]
    public void ActiveArtworkClick_IsSeparateFromSelectionChanges()
    {
        var items = CreateItems(3, ArtworkShape.Wide);
        ArtworkStackItem? opened = null;
        ArtworkStackItem? selected = null;
        var cut = RenderComponent<MediaArtworkCarousel>(parameters => parameters
            .Add(component => component.Items, items)
            .Add(component => component.SelectedItemId, items[1].Id)
            .Add(component => component.SelectedItemChanged, EventCallback.Factory.Create<ArtworkStackItem>(this, (ArtworkStackItem item) => selected = item))
            .Add(component => component.ActiveItemClicked, EventCallback.Factory.Create<ArtworkStackItem>(this, (ArtworkStackItem item) => opened = item))
            .Add(component => component.ItemNoun, "episode"));

        cut.Find("button[aria-label='Open Item 2']").Click();
        Assert.Same(items[1], opened);
        Assert.Null(selected);
    }

    [Theory]
    [InlineData(2, "has-two", ArtworkShape.Portrait, "is-portrait-cluster")]
    [InlineData(3, "has-three", ArtworkShape.Square, "is-square-cluster")]
    [InlineData(4, "has-four", ArtworkShape.Wide, "is-wide-cluster")]
    [InlineData(6, "has-more", ArtworkShape.Portrait, "is-portrait-cluster")]
    public void GroupPreview_UsesCountAndShapeSpecificLayouts(
        int count,
        string countClass,
        ArtworkShape shape,
        string shapeClass)
    {
        var items = CreateItems(count, shape);
        var cut = RenderComponent<MediaArtworkGroupPreview>(parameters => parameters
            .Add(component => component.Items, items)
            .Add(component => component.TotalCount, count));

        var root = cut.Find(".media-artwork-group-preview");
        Assert.Contains(countClass, root.ClassList);
        Assert.Contains(shapeClass, root.ClassList);
        Assert.Equal(Math.Min(4, count), cut.FindAll(".media-artwork-group-preview__artwork").Count);

        if (count > 4)
        {
            Assert.Equal($"+{count - 4}", cut.Find(".media-artwork-group-preview__overflow").TextContent);
        }
    }

    [Fact]
    public void GroupPreview_MixedArtworkUsesMixedLayout()
    {
        var items = new[]
        {
            CreateItem(1, ArtworkShape.Portrait),
            CreateItem(2, ArtworkShape.Square),
            CreateItem(3, ArtworkShape.Wide),
        };
        var cut = RenderComponent<MediaArtworkGroupPreview>(parameters => parameters
            .Add(component => component.Items, items));

        Assert.Contains("is-mixed", cut.Find(".media-artwork-group-preview").ClassList);
    }

    [Fact]
    public void SharedCarousel_HasStableTileAndDetailDensityContracts()
    {
        var item = CreateItem(1, ArtworkShape.Portrait);
        var cut = RenderComponent<MediaArtworkCarousel>(parameters => parameters
            .Add(component => component.Items, new[] { item })
            .Add(component => component.Density, MediaArtworkCarouselDensity.Detail));

        Assert.Contains("is-detail", cut.Find(".media-artwork-carousel").ClassList);

        var repoRoot = FindRepoRoot();
        var css = File.ReadAllText(Path.Combine(repoRoot, "src/MediaEngine.Web/Components/Shared/MediaArtworkCarousel.razor.css"));
        Assert.Contains("grid-template-rows: minmax(0, 1fr) auto auto", css, StringComparison.Ordinal);
        Assert.Contains(".media-artwork-carousel.is-detail", css, StringComparison.Ordinal);
        Assert.Contains("prefers-reduced-motion: reduce", css, StringComparison.Ordinal);
    }

    [Fact]
    public void GroupTile_RapidSwapsWrapWithoutChangingTheCardOrNavigating()
    {
        var items = CreateItems(3, ArtworkShape.Portrait)
            .Select((item, index) => new ArtworkStackItem
            {
                Id = item.Id,
                WorkId = item.WorkId,
                Title = item.Title,
                ImageUrl = item.ImageUrl,
                MediaType = "Book",
                NavigationUrl = $"/book/{index + 1}",
                Shape = item.Shape,
                Position = item.Position,
            })
            .ToList();
        var group = new MediaTileViewModel
        {
            Id = Guid.NewGuid(),
            CollectionId = Guid.NewGuid(),
            Title = "Rapid Series",
            Description = "A stable test series.",
            MediaKind = "Book",
            Shape = MediaTileShape.Landscape,
            Presentation = MediaTilePresentation.BookSeries,
            HoverMode = MediaTileHoverMode.Expanded,
            NavigationUrl = "/details/bookseries/rapid",
            PrimaryNavigationUrl = "/details/bookseries/rapid",
            UseLandscapeGroupTile = true,
            PreviewTotalCount = items.Count,
            ArtworkStackItems = items,
        };
        var cut = RenderComponent<MediaGroupTile>(parameters => parameters
            .Add(component => component.Item, group));
        var root = cut.Find("article.media-group-tile");
        var scopedAttribute = root.Attributes.Single(attribute => attribute.Name.StartsWith("b-", StringComparison.Ordinal)).Name;
        var nav = Services.GetRequiredService<NavigationManager>();
        var originalUri = nav.Uri;

        var next = cut.Find("button[aria-label='Next book']");
        next.Click();
        cut.Find("button[aria-label='Next book']").Click();
        cut.Find("button[aria-label='Next book']").Click();
        cut.Find("button[aria-label='Next book']").Click();

        Assert.Contains("Item 2", cut.Find(".media-artwork-carousel__caption").TextContent);
        Assert.Equal(originalUri, nav.Uri);
        Assert.Equal(scopedAttribute, cut.Find("article.media-group-tile").Attributes.Single(attribute => attribute.Name.StartsWith("b-", StringComparison.Ordinal)).Name);
        Assert.Single(cut.FindAll("article.media-group-tile"));
        Assert.Single(cut.FindAll(".media-artwork-carousel__active-artwork"));
        Assert.Equal(2, cut.FindAll(".media-artwork-carousel__side-artwork").Count);
    }

    private IRenderedComponent<MediaArtworkCarousel> RenderCarousel(
        IReadOnlyList<ArtworkStackItem> items,
        string selectedItemId,
        Action<ArtworkStackItem> onSelected) =>
        RenderComponent<MediaArtworkCarousel>(parameters => parameters
            .Add(component => component.Items, items)
            .Add(component => component.SelectedItemId, selectedItemId)
            .Add(component => component.SelectedItemChanged, EventCallback.Factory.Create(this, onSelected))
            .Add(component => component.ItemNoun, "book")
            .Add(component => component.Circular, true));

    private static IReadOnlyList<ArtworkStackItem> CreateItems(int count, ArtworkShape shape) =>
        Enumerable.Range(1, count).Select(index => CreateItem(index, shape)).ToList();

    private static ArtworkStackItem CreateItem(int index, ArtworkShape shape) => new()
    {
        Id = index.ToString(),
        WorkId = Guid.NewGuid(),
        Title = $"Item {index}",
        ImageUrl = $"/art/{index}.jpg",
        Shape = shape,
        Position = index.ToString(),
    };

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
