namespace MediaEngine.Web.Tests;

public sealed class ViewLibrarySurfaceTests
{
    [Fact]
    public void ViewSurface_ExposesMixedLocalLibraryWithoutCatalogueActions()
    {
        var source = Read("src/MediaEngine.Web/Components/Pages/ViewPage.razor");

        Assert.Contains("@page \"/view\"", source, StringComparison.Ordinal);
        Assert.Contains("@page \"/view/{LibraryId:guid}\"", source, StringComparison.Ordinal);
        Assert.Contains("<ViewSectionShell>", source, StringComparison.Ordinal);
        Assert.Contains("<PageTitle>Photos - Tuvima</PageTitle>", source, StringComparison.Ordinal);
        Assert.Contains("Search names, dates, devices, locations, and tags", source, StringComparison.Ordinal);
        Assert.Contains("role=\"tablist\"", source, StringComparison.Ordinal);
        Assert.Contains("aria-modal=\"true\"", source, StringComparison.Ordinal);
        Assert.Contains("ToggleFavoriteAsync", source, StringComparison.Ordinal);
        Assert.Contains("ToggleHiddenAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("view-tile__actions", source, StringComparison.Ordinal);
        Assert.Contains("IsVideo", source, StringComparison.Ordinal);
        Assert.Contains("IsDocument", source, StringComparison.Ordinal);
        Assert.Contains("IsAudio", source, StringComparison.Ordinal);
        Assert.Contains("profileId=", source, StringComparison.Ordinal);
        Assert.Contains("SupplyParameterFromQuery", source, StringComparison.Ordinal);
        Assert.Contains("Add media", source, StringComparison.Ordinal);
        Assert.Contains("<InputFile", source, StringComparison.Ordinal);
        Assert.Contains("UploadViewMediaAsync", source, StringComparison.Ordinal);
        Assert.Contains("role=\"status\"", source, StringComparison.Ordinal);
        Assert.Contains("role=\"alert\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("@page \"/photos\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Wikidata", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Provider", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MediaEditorLauncherService", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ViewWorkspace_UsesOnlyFourPrimaryDestinationsAcrossRealRoutes()
    {
        var shell = Read("src/MediaEngine.Web/Components/Pages/ViewSectionShell.razor");

        Assert.Contains("new(\"Photos\", \"/view\"", shell, StringComparison.Ordinal);
        Assert.Contains("new(\"Galleries\", \"/view/galleries\"", shell, StringComparison.Ordinal);
        Assert.Contains("new(\"People\", \"/view/people\"", shell, StringComparison.Ordinal);
        Assert.Contains("new(\"Places\", \"/view/places\"", shell, StringComparison.Ordinal);
        Assert.Equal(4, shell.Split("Exact: true", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("Libraries", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("Favorites", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("Videos", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("Archive", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("Trash", shell, StringComparison.Ordinal);

        AssertViewRoute("ViewGalleriesPage.razor", "/view/galleries");
        AssertViewRoute("ViewPeoplePage.razor", "/view/people");
        AssertViewRoute("ViewPlacesPage.razor", "/view/places");
    }

    [Fact]
    public void ViewCapabilityPages_DoNotPresentSyntheticMedia()
    {
        var galleries = Read("src/MediaEngine.Web/Components/Pages/ViewGalleriesPage.razor");
        var people = Read("src/MediaEngine.Web/Components/Pages/ViewPeoplePage.razor");
        var places = Read("src/MediaEngine.Web/Components/Pages/ViewPlacesPage.razor");

        Assert.Contains("AppPageStateKind.Empty", galleries, StringComparison.Ordinal);
        Assert.Contains("AppPageStateKind.Unavailable", people, StringComparison.Ordinal);
        Assert.Contains("will not invent people or matches", people, StringComparison.Ordinal);
        Assert.Contains("AppPageStateKind.Empty", places, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach", galleries, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("foreach", people, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("foreach", places, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ListenPlaylistRail_UsesTypedPlaylistDropWithoutChangingPlaylistBehavior()
    {
        var listen = Read("src/MediaEngine.Web/Components/Pages/ListenBrowsePage.razor");

        Assert.Contains("DropTarget: new PlaylistNavigationDropTarget(playlist.Id)", listen, StringComparison.Ordinal);
        Assert.Contains("MediaSectionNavigationDropEvent dropEvent", listen, StringComparison.Ordinal);
        Assert.Contains("dropEvent.Target is not PlaylistNavigationDropTarget playlistTarget", listen, StringComparison.Ordinal);
        Assert.Contains("AudioDrag.WorkIds", listen, StringComparison.Ordinal);
        Assert.Contains("ApiClient.AddCollectionItemAsync", listen, StringComparison.Ordinal);
        Assert.Contains("AudioDrag.Clear()", listen, StringComparison.Ordinal);
        Assert.DoesNotContain("DropCollectionId", listen, StringComparison.Ordinal);
    }

    [Fact]
    public void ViewStyles_UseResponsiveStableMixedMediaGeometry()
    {
        var styles = Read("src/MediaEngine.Web/Components/Pages/ViewPage.razor.css");

        Assert.Contains("aspect-ratio: 4 / 3;", styles, StringComparison.Ordinal);
        Assert.Contains("object-fit: cover;", styles, StringComparison.Ordinal);
        Assert.Contains(":focus-visible", styles, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 900px)", styles, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 640px)", styles, StringComparison.Ordinal);
    }

    private static string Read(string relativePath)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "MediaEngine.slnx")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        return File.ReadAllText(Path.Combine(root.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static void AssertViewRoute(string fileName, string route)
    {
        var source = Read($"src/MediaEngine.Web/Components/Pages/{fileName}");
        Assert.Contains($"@page \"{route}\"", source, StringComparison.Ordinal);
        Assert.Contains("<ViewSectionShell>", source, StringComparison.Ordinal);
    }
}
