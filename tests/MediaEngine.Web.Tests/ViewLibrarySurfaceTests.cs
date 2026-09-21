namespace MediaEngine.Web.Tests;

public sealed class ViewLibrarySurfaceTests
{
    [Fact]
    public void ViewPhotos_UsesPersonalTimelineWithoutPhysicalLibraryControls()
    {
        var source = Read("src/MediaEngine.Web/Components/Pages/ViewPage.razor");

        Assert.Contains("@page \"/view\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("@page \"/view/{LibraryId:guid}\"", source, StringComparison.Ordinal);
        Assert.Contains("<ViewSectionShell", source, StringComparison.Ordinal);
        Assert.Contains("<PageTitle>Photos - Tuvima Library</PageTitle>", source, StringComparison.Ordinal);
        Assert.Contains("Search photos, dates, devices, locations, and tags", source, StringComparison.Ordinal);
        Assert.Contains("role=\"tablist\"", source, StringComparison.Ordinal);
        Assert.Contains("<ViewImmersiveViewer", source, StringComparison.Ordinal);
        Assert.Contains("ToggleFavoriteAsync", source, StringComparison.Ordinal);
        Assert.Contains("ArchiveViewItemAsync", source, StringComparison.Ordinal);
        Assert.Contains("TrashViewItemAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("view-tile__actions", source, StringComparison.Ordinal);
        Assert.Contains("GetViewAssetsAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("profileId=", source, StringComparison.Ordinal);
        Assert.Contains("ViewMediaGrantService", source, StringComparison.Ordinal);
        Assert.Contains("/view-media/{grant.Value}", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ToAbsoluteEngineUrl", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SupplyParameterFromQuery", source, StringComparison.Ordinal);
        Assert.Contains("Photos quick filters", source, StringComparison.Ordinal);
        Assert.Contains("[\"all\"] = \"All\"", source, StringComparison.Ordinal);
        Assert.Contains("[\"favorites\"] = \"Favorites\"", source, StringComparison.Ordinal);
        Assert.Contains("[\"videos\"] = \"Videos\"", source, StringComparison.Ordinal);
        Assert.Contains("[\"archive\"] = \"Archive\"", source, StringComparison.Ordinal);
        Assert.Contains("Add media", source, StringComparison.Ordinal);
        Assert.Contains("<InputFile", source, StringComparison.Ordinal);
        Assert.Contains("UploadViewMediaAsync", source, StringComparison.Ordinal);
        Assert.Contains("role=\"status\"", source, StringComparison.Ordinal);
        Assert.Contains("role=\"alert\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("view-library-picker", source, StringComparison.Ordinal);
        Assert.DoesNotContain("view-summary", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Scan library", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedLibrary.Name", source, StringComparison.Ordinal);
        Assert.DoesNotContain("@page \"/photos\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Wikidata", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Provider", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MediaEditorLauncherService", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ViewWorkspace_ExposesArtworkAlongsidePersonalMediaDestinations()
    {
        var shell = Read("src/MediaEngine.Web/Components/Pages/ViewSectionShell.razor");

        Assert.Contains("new(\"Photos\", \"/view\"", shell, StringComparison.Ordinal);
        Assert.Contains("new(\"Galleries\", \"/view/galleries\"", shell, StringComparison.Ordinal);
        Assert.Contains("new(\"Artwork\", \"/view/artwork\"", shell, StringComparison.Ordinal);
        Assert.Contains("new(\"People\", \"/view/people\"", shell, StringComparison.Ordinal);
        Assert.Contains("new(\"Places\", \"/view/places\"", shell, StringComparison.Ordinal);
        Assert.Contains("IsSmart: gallery.Kind == ViewGalleryKind.Smart", shell, StringComparison.Ordinal);
        Assert.Contains("DropTarget: gallery.Kind == ViewGalleryKind.Manual", shell, StringComparison.Ordinal);
        Assert.Contains("new SmartGalleryCreateTarget", shell, StringComparison.Ordinal);
        Assert.Equal(1, shell.Split("new(\"Photos\", \"/view\"", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("Libraries", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("Favorites", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("Videos", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("Archive", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("Trash", shell, StringComparison.Ordinal);

        AssertViewRoute("ViewGalleriesPage.razor", "/view/galleries");
        AssertViewRoute("ViewArtworkPage.razor", "/view/artwork");
        AssertViewRoute("ViewPeoplePage.razor", "/view/people");
        AssertViewRoute("ViewPlacesPage.razor", "/view/places");
    }

    [Fact]
    public void ViewArtwork_UsesPagedVirtualGalleriesAndExistingEditors()
    {
        var artwork = Read("src/MediaEngine.Web/Components/Pages/ViewArtworkPage.razor");
        var client = Read("src/MediaEngine.Web/Services/Integration/EngineApiClient.Artwork.cs");
        var readModel = Read("src/MediaEngine.Api/Services/ReadServices/ArtworkLibraryReadService.cs");

        Assert.Contains("Library Artwork", artwork, StringComparison.Ordinal);
        Assert.Contains("GetArtworkLibraryAsync", artwork, StringComparison.Ordinal);
        Assert.Contains("LoadMoreAsync", artwork, StringComparison.Ordinal);
        Assert.Contains("Series / Groups", artwork, StringComparison.Ordinal);
        Assert.Contains("TV Shows", artwork, StringComparison.Ordinal);
        Assert.Contains("Albums", artwork, StringComparison.Ordinal);
        Assert.Contains("ArtworkResolutionMode.AutomaticGroup", artwork, StringComparison.Ordinal);
        Assert.Contains("MediaArtworkGroupPreview", artwork, StringComparison.Ordinal);
        Assert.Contains("ArtworkWorkspaceDialog", artwork, StringComparison.Ordinal);
        Assert.Contains("Restore automatic artwork", Read("src/MediaEngine.Web/Components/Artwork/ArtworkWorkspaceDialog.razor"), StringComparison.Ordinal);
        Assert.Contains("MediaEditorLauncher.OpenAsync", artwork, StringComparison.Ordinal);
        Assert.Contains("InitialTab = \"artwork\"", artwork, StringComparison.Ordinal);
        Assert.Contains("People images", artwork, StringComparison.Ordinal);
        Assert.Contains("Drop image here", Read("src/MediaEngine.Web/Components/MediaEditor/SharedMediaEditorShell.razor"), StringComparison.Ordinal);
        Assert.Contains("Drop image here", Read("src/MediaEngine.Web/Components/MediaEditor/PersonEditorDialog.razor"), StringComparison.Ordinal);
        Assert.Contains("/api/v1/display/artwork", client, StringComparison.Ordinal);
        Assert.Contains("generation != _loadGeneration", artwork, StringComparison.Ordinal);
        Assert.Contains("primary_person_media_credits", readModel, StringComparison.Ordinal);
        Assert.Contains("CollectionGroupByField", readModel, StringComparison.Ordinal);
        Assert.DoesNotContain("Wikidata", artwork, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ViewPrimaryRoutes_ShareTheLaneAlignedContentFrame()
    {
        string[] routes =
        [
            "ViewPage.razor",
            "ViewFoldersPage.razor",
            "ViewGalleriesPage.razor",
            "ViewArtworkPage.razor",
            "ViewPeoplePage.razor",
            "ViewPlacesPage.razor",
        ];

        foreach (var route in routes)
        {
            Assert.Contains("<ViewContentPage", Read($"src/MediaEngine.Web/Components/Pages/{route}"), StringComparison.Ordinal);
        }

        var frame = Read("src/MediaEngine.Web/Components/Pages/ViewContentPage.razor.css");
        Assert.Contains("align-content: start", frame, StringComparison.Ordinal);
        Assert.Contains("font-family: var(--font-ui, inherit)", frame, StringComparison.Ordinal);
        Assert.Contains("padding: 28px clamp(20px, 2.2vw, 40px) 48px", frame, StringComparison.Ordinal);
    }

    [Fact]
    public void ViewCapabilityPages_DoNotPresentSyntheticMedia()
    {
        var galleries = Read("src/MediaEngine.Web/Components/Pages/ViewGalleriesPage.razor");
        var galleryEditor = Read("src/MediaEngine.Web/Components/Collections/GalleryEditorShell.razor");
        var people = Read("src/MediaEngine.Web/Components/Pages/ViewPeoplePage.razor");
        var places = Read("src/MediaEngine.Web/Components/Pages/ViewPlacesPage.razor");

        Assert.Contains("GalleryEditorLauncher.OpenAsync", galleries, StringComparison.Ordinal);
        Assert.Contains("CreateViewGalleryAsync", galleryEditor, StringComparison.Ordinal);
        Assert.Contains("ViewRuleBuilder", galleryEditor, StringComparison.Ordinal);
        Assert.Contains("ViewDiscoveryCapabilityStates", people, StringComparison.Ordinal);
        Assert.Contains("GetViewPeopleAsync", people, StringComparison.Ordinal);
        Assert.Contains("AppPageStateKind.Empty", places, StringComparison.Ordinal);
        Assert.Contains("GetViewPlacesAsync", places, StringComparison.Ordinal);
        Assert.DoesNotContain("fake", galleries, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ViewPhotosAndGalleries_ExposeTypedSelectionAndManualOnlyDrops()
    {
        var photos = Read("src/MediaEngine.Web/Components/Pages/ViewPage.razor");
        var timeline = Read("src/MediaEngine.Web/Components/Pages/ViewPhotoTimeline.razor");
        var galleries = Read("src/MediaEngine.Web/Components/Pages/ViewGalleriesPage.razor");
        var galleryEditor = Read("src/MediaEngine.Web/Components/Collections/GalleryEditorShell.razor");
        var shell = Read("src/MediaEngine.Web/Components/Pages/ViewSectionShell.razor");

        Assert.Contains("ViewSelectionToolbar", photos, StringComparison.Ordinal);
        Assert.Contains("OnSelectDateGroup", timeline, StringComparison.Ordinal);
        Assert.Contains("DurationSeconds", timeline, StringComparison.Ordinal);
        Assert.Contains("Density=\"@Workspace.Density\"", photos, StringComparison.Ordinal);
        Assert.Contains("view-density--@Density", timeline, StringComparison.Ordinal);
        Assert.Contains("view-tile__select-control", timeline, StringComparison.Ordinal);
        Assert.DoesNotContain("<AppCheckbox", timeline, StringComparison.Ordinal);
        Assert.Contains("ViewGalleryKind.Manual", shell, StringComparison.Ordinal);
        Assert.Contains("ViewGalleryKind.Smart", galleries, StringComparison.Ordinal);
        Assert.Contains("<ViewRuleBuilder", galleryEditor, StringComparison.Ordinal);
        Assert.Contains(".Take(12)", shell, StringComparison.Ordinal);
        Assert.Contains("new ManualGalleryNavigationDropTarget", shell, StringComparison.Ordinal);
        Assert.Contains("new NewGalleryNavigationDropTarget", shell, StringComparison.Ordinal);
        Assert.Contains("finally", shell, StringComparison.Ordinal);
        Assert.Contains("AssetDrag.Clear()", shell, StringComparison.Ordinal);
    }

    [Fact]
    public void ViewScopeRequests_KeepResolvedMineOwnerOutOfProfileOnlyParameter()
    {
        var workspace = Read("src/MediaEngine.Web/Services/Integration/ViewWorkspaceService.cs");
        var client = Read("src/MediaEngine.Web/Services/Integration/EngineApiClient.View.cs");

        Assert.Contains("ScopeKind == ViewScopeKind.Profile", workspace, StringComparison.Ordinal);
        Assert.Contains("options.Scope == ViewScopeKind.Profile", client, StringComparison.Ordinal);
        Assert.Contains("scope == ViewScopeKind.Profile", client, StringComparison.Ordinal);
    }

    [Fact]
    public void GalleryDetail_ProvidesOwnerManagementAndKeepsSmartMembershipRuleOnly()
    {
        var detail = Read("src/MediaEngine.Web/Components/Pages/ViewGalleryDetailPage.razor");
        var editor = Read("src/MediaEngine.Web/Components/Collections/GalleryEditorShell.razor");

        Assert.Contains("private bool IsOwner", detail, StringComparison.Ordinal);
        Assert.Contains("GetViewGalleryShareTargetsAsync", detail, StringComparison.Ordinal);
        Assert.Contains("GetViewGallerySharesAsync", detail, StringComparison.Ordinal);
        Assert.Contains("ReplaceViewGallerySharesAsync", detail, StringComparison.Ordinal);
        Assert.Contains("GalleryEditorLauncher.OpenAsync", detail, StringComparison.Ordinal);
        Assert.Contains("UpdateViewGalleryAsync", editor, StringComparison.Ordinal);
        Assert.Contains("DeleteViewGalleryAsync", editor, StringComparison.Ordinal);
        Assert.Contains("DeleteViewGalleryAsync", Read("src/MediaEngine.Web/Components/Pages/ViewSectionShell.razor"), StringComparison.Ordinal);
        Assert.Contains("<AppDialog", detail, StringComparison.Ordinal);
        Assert.Contains("<ViewRuleBuilder", editor, StringComparison.Ordinal);
        Assert.Contains("_gallery.Kind == ViewGalleryKind.Manual", detail, StringComparison.Ordinal);
        Assert.Contains("Manual membership stays available as a quick action", editor, StringComparison.Ordinal);
        Assert.Contains("(\"membership\", \"Membership\"", editor, StringComparison.Ordinal);
        Assert.DoesNotContain("Label=\"Edit\"", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("Label=\"Delete\"", detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ListenPlaylistRail_UsesTypedPlaylistDropWithoutChangingPlaylistBehavior()
    {
        var listen = Read("src/MediaEngine.Web/Components/Pages/ListenBrowsePage.razor");

        Assert.Contains("DropTarget: IsManualPlaylist(playlist) ? new PlaylistNavigationDropTarget(playlist.Id) : null", listen, StringComparison.Ordinal);
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
        Assert.Contains("<ViewSectionShell", source, StringComparison.Ordinal);
    }
}
