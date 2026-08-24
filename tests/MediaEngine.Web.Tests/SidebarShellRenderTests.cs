using Bunit;
using MediaEngine.Web.Components.MediaHub;
using MediaEngine.Web.Components.Pages;
using MediaEngine.Web.Models.ViewDTOs;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace MediaEngine.Web.Tests;

public sealed class SidebarShellRenderTests : AsyncBunitContext
{
    public SidebarShellRenderTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
    }

    [Fact]
    public void MediaSectionShell_PreservesFlatLaneNavigation()
    {
        var navigation = new[]
        {
            new MediaSectionNavigationGroup("Library",
            [
                new("Discover", "/read", Icons.Material.Outlined.Explore, Exact: true),
                new("Books", "/read/books", Icons.Material.Outlined.MenuBook),
            ]),
        };

        var cut = Render<MediaSectionShell>(parameters => parameters
            .Add(component => component.Title, "Read")
            .Add(component => component.NavigationGroups, navigation)
            .AddChildContent("<section id=\"content-slot\">Content</section>"));

        Assert.Single(cut.FindAll(".media-section-shell"));
        Assert.Single(cut.FindAll(".media-section-shell__rail"));
        Assert.Single(cut.FindAll(".media-section-shell__content"));
        Assert.Equal(2, cut.FindAll("a.media-section-shell__rail-item").Count);
        Assert.Empty(cut.FindAll(".media-section-shell__rail-item--parent"));
        Assert.Empty(cut.FindAll(".media-section-shell__rail-chevron"));
        Assert.NotNull(cut.Find("#content-slot"));
    }

    [Fact]
    public void ViewSectionShell_RendersExactlyFourPrimaryDestinationsAndTracksRoute()
    {
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/view");
        var cut = Render<ViewSectionShell>(parameters => parameters
            .AddChildContent("<section id=\"view-slot\">View content</section>"));

        Assert.Equal(4, cut.FindAll(".media-section-shell__rail-group > .media-section-shell__rail-item").Count);
        Assert.Equal("Photos", cut.Find("#media-section-nav-view").TextContent.Trim());
        Assert.Equal("Galleries", cut.Find("#media-section-nav-view-galleries").TextContent.Trim());
        Assert.Equal("People", cut.Find("#media-section-nav-view-people").TextContent.Trim());
        Assert.Equal("Places", cut.Find("#media-section-nav-view-places").TextContent.Trim());
        Assert.Equal("page", cut.Find("#media-section-nav-view").GetAttribute("aria-current"));

        navigationManager.NavigateTo("/view/places");

        cut.WaitForAssertion(() =>
            Assert.Equal("page", cut.Find("#media-section-nav-view-places").GetAttribute("aria-current")));
        Assert.NotNull(cut.Find("#view-slot"));
    }

    [Fact]
    public void MediaSectionShell_ExpandsActiveBranchAndMarksDeepLink()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("/settings/ai/models");
        var cut = Render<MediaSectionShell>(parameters => parameters
            .Add(component => component.Title, "Settings")
            .Add(component => component.AccordionNavigation, true)
            .Add(component => component.NavigationGroups, BuildNestedNavigation())
            .AddChildContent("<section>Settings content</section>"));

        var localAi = cut.Find("#media-section-nav-settings-ai");
        var models = cut.Find("#media-section-nav-settings-ai-models");

        Assert.Contains("media-section-shell--nested", cut.Find(".media-section-shell").ClassList);
        Assert.Equal("A", localAi.TagName);
        Assert.Equal("true", localAi.GetAttribute("aria-expanded"));
        Assert.Equal("page", models.GetAttribute("aria-current"));
        Assert.Contains("is-active", models.ClassList);
        Assert.Equal(2, cut.FindAll(".media-section-shell__rail-item--child").Count);
        Assert.Empty(cut.FindAll(".media-section-shell__rail-item--child .mud-icon-root"));
    }

    [Fact]
    public void MediaSectionShell_AccordionOpensSelectedParentAndClosesPreviousBranch()
    {
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/settings/ai/models");
        var cut = Render<MediaSectionShell>(parameters => parameters
            .Add(component => component.Title, "Settings")
            .Add(component => component.AccordionNavigation, true)
            .Add(component => component.NavigationGroups, BuildNestedNavigation())
            .AddChildContent("<section>Settings content</section>"));

        cut.Find("#media-section-nav-settings-providers").Click();

        Assert.Equal("false", cut.Find("#media-section-nav-settings-ai").GetAttribute("aria-expanded"));
        Assert.Equal("true", cut.Find("#media-section-nav-settings-providers").GetAttribute("aria-expanded"));
        Assert.EndsWith("/settings/providers", navigationManager.Uri, StringComparison.Ordinal);
        Assert.Single(cut.FindAll(".media-section-shell__rail-children"));
    }

    [Fact]
    public void MediaSectionShell_DeliversTypedPlaylistDropEvent()
    {
        var playlistId = Guid.NewGuid();
        MediaSectionNavigationDropEvent? received = null;
        var navigation = new[]
        {
            new MediaSectionNavigationGroup("Playlists",
            [
                new("Road trip", "/listen/music/playlists/road-trip", Icons.Material.Outlined.PlaylistPlay,
                    DropTarget: new PlaylistNavigationDropTarget(playlistId)),
            ]),
        };

        var cut = Render<MediaSectionShell>(parameters => parameters
            .Add(component => component.Title, "Listen")
            .Add(component => component.NavigationGroups, navigation)
            .Add(component => component.OnNavigationItemDrop,
                EventCallback.Factory.Create<MediaSectionNavigationDropEvent>(this, value => received = value))
            .AddChildContent("<section>Listen content</section>"));

        var target = cut.Find(".media-section-shell__rail-item.is-drop-target");
        target.TriggerEvent("ondrop", new DragEventArgs());

        var playlistTarget = Assert.IsType<PlaylistNavigationDropTarget>(received?.Target);
        Assert.Equal(playlistId, playlistTarget.PlaylistId);
        Assert.Equal("Road trip", received?.Item.Label);
    }

    [Fact]
    public void MediaSectionShell_ExpandsForDragAndAllowsNestedGalleryDropTargets()
    {
        var galleryId = Guid.NewGuid();
        MediaSectionNavigationDropEvent? received = null;
        var navigation = new[]
        {
            new MediaSectionNavigationGroup("View",
            [
                new("Galleries", "/view/galleries", Icons.Material.Outlined.Collections,
                    Children:
                    [
                        new("Family", $"/view/galleries/{galleryId:D}", Icons.Material.Outlined.PhotoAlbum,
                            DropTarget: new ManualGalleryNavigationDropTarget(galleryId)),
                        new("New Gallery", "/view/galleries/new", Icons.Material.Outlined.Add,
                            DropTarget: new NewGalleryNavigationDropTarget()),
                    ]),
            ]),
        };

        var cut = Render<MediaSectionShell>(parameters => parameters
            .Add(component => component.Title, "View")
            .Add(component => component.NavigationGroups, navigation)
            .Add(component => component.OnNavigationItemDrop,
                EventCallback.Factory.Create<MediaSectionNavigationDropEvent>(this, value => received = value))
            .AddChildContent("<section>View content</section>"));

        cut.Find("#media-section-nav-view-galleries").TriggerEvent("ondragenter", new DragEventArgs());
        var nestedTarget = cut.Find($"#media-section-nav-view-galleries-{galleryId:D}");
        nestedTarget.TriggerEvent("ondrop", new DragEventArgs());

        var galleryTarget = Assert.IsType<ManualGalleryNavigationDropTarget>(received?.Target);
        Assert.Equal(galleryId, galleryTarget.GalleryId);
        Assert.Equal(2, cut.FindAll(".media-section-shell__rail-item--child.is-drop-target").Count);
    }

    private static IReadOnlyList<MediaSectionNavigationGroup> BuildNestedNavigation() =>
    [
        new("Admin Settings",
        [
            new("Local AI", "/settings/ai", Icons.Material.Outlined.Memory,
                Children:
                [
                    new("Overview", "/settings/ai/overview", Icons.Material.Outlined.Dashboard, Exact: true),
                    new("Models", "/settings/ai/models", Icons.Material.Outlined.Storage, Exact: true),
                ]),
            new("Providers", "/settings/providers", Icons.Material.Outlined.Storage,
                Children:
                [
                    new("Retail Lookup", "/settings/providers/retail", Icons.Material.Outlined.ShoppingBag, Exact: true),
                    new("Canonical Identity", "/settings/providers/canonical", Icons.Material.Outlined.Hub, Exact: true),
                ]),
        ]),
    ];
}
