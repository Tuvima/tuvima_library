using MediaEngine.Contracts.Collections;
using MediaEngine.Web.Models.ViewDTOs;
using MediaEngine.Web.Services.MediaTiles;

namespace MediaEngine.Web.Tests;

public sealed class CollectionsHubTests
{
    [Fact]
    public void MainLayout_OrdersPrimaryNavigationByMediaThenCollections()
    {
        var source = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Shared\MainLayout.razor"));

        var readIndex = source.IndexOf("new(\"/read\", \"Nav_Read\")", StringComparison.Ordinal);
        var watchIndex = source.IndexOf("new(\"/watch\", \"Nav_Watch\")", StringComparison.Ordinal);
        var listenIndex = source.IndexOf("new(\"/listen\", \"Nav_Listen\")", StringComparison.Ordinal);
        var viewIndex = source.IndexOf("new(\"/view\", \"Nav_View\")", StringComparison.Ordinal);
        var collectionsIndex = source.IndexOf("new(\"/collections\", \"Nav_Collections\")", StringComparison.Ordinal);

        Assert.True(readIndex >= 0);
        Assert.True(watchIndex > readIndex);
        Assert.True(listenIndex > watchIndex);
        Assert.True(viewIndex > listenIndex);
        Assert.True(collectionsIndex > viewIndex);
    }

    [Fact]
    public void CollectionsPage_UsesSharedSectionArchitectureAndTypedCatalogs()
    {
        var source = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Collections\CollectionsPage.razor"));
        var routeSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Pages\Collections.razor"));
        var configurationSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Collections\CollectionsSectionConfiguration.cs"));
        var headerSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\MediaHub\LibrarySectionHeader.razor"));
        var composerSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Services\MediaTiles\CollectionSurfaceTileComposer.cs"));
        var peopleClientSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Services\Integration\EngineApiClient.People.cs"));
        var peopleListSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Collections\PeopleCatalogList.razor"));
        var browseShellSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Browse\MediaBrowseShell.razor"));
        var browseShellStylesSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Browse\BrowseShellStyles.razor.css"));
        var tileGridSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\MediaTiles\MediaTileGrid.razor"));
        var groupTileSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\MediaTiles\MediaGroupTile.razor"));
        var groupTileStylesSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\MediaTiles\MediaGroupTile.razor.css"));
        var styles = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Collections\CollectionsSectionLayout.razor.css"));
        var sectionShellStyles = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\MediaHub\MediaSectionShell.razor.css"));

        Assert.Contains("@page \"/collections/{Section}\"", routeSource, StringComparison.Ordinal);
        Assert.Contains("[SupplyParameterFromQuery(Name = \"lane\")]", routeSource, StringComparison.Ordinal);
        Assert.Contains("LaneQuery=\"@LaneQuery\"", routeSource, StringComparison.Ordinal);
        Assert.Contains("<MediaSectionShell", source, StringComparison.Ordinal);
        Assert.Contains("<LibrarySectionHeader", source, StringComparison.Ordinal);
        Assert.Contains("<SurfaceNavigationBar", headerSource, StringComparison.Ordinal);
        Assert.Contains("new(Overview, \"Discovery\", \"/collections\")", configurationSource, StringComparison.Ordinal);
        Assert.Contains("new(Automatic, \"Automatic\", \"/collections/automatic\")", configurationSource, StringComparison.Ordinal);
        Assert.Contains("new(Curated, \"Curated\", \"/collections/curated\")", configurationSource, StringComparison.Ordinal);
        Assert.Contains("new(Shelves, \"Shelves\", \"/collections/shelves\")", configurationSource, StringComparison.Ordinal);
        Assert.Contains("new(People, \"People\", \"/collections/people\")", configurationSource, StringComparison.Ordinal);
        Assert.Contains("new(\"Browse library\"", configurationSource, StringComparison.Ordinal);
        Assert.Contains("new(\"Shortcuts\"", configurationSource, StringComparison.Ordinal);
        Assert.Contains("GetCollectionCatalogAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetContributorShelvesAsync", source, StringComparison.Ordinal);
        Assert.Contains("GetContentGroupsAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetSystemViewGroupsAsync", source, StringComparison.Ordinal);
        Assert.Contains("GetPersonsPageAsync", source, StringComparison.Ordinal);
        Assert.Contains("GetPersonPresenceAsync", source, StringComparison.Ordinal);
        Assert.Contains("padding: 10px var(--collections-content-gutter) 56px", styles, StringComparison.Ordinal);
        Assert.Contains("--collections-content-gutter: 12px", styles, StringComparison.Ordinal);
        Assert.Contains("height: calc(100dvh - var(--app-topbar-height, 65px) - 4rem)", sectionShellStyles, StringComparison.Ordinal);
        Assert.Contains("overflow-x: hidden", sectionShellStyles, StringComparison.Ordinal);
        Assert.Contains("/persons?catalog=true", peopleClientSource, StringComparison.Ordinal);
        Assert.Contains("/persons/role-counts?catalog=true", peopleClientSource, StringComparison.Ordinal);
        Assert.Contains("CollectionSurfaceTileComposer.FromCollection", source, StringComparison.Ordinal);
        Assert.Contains("CollectionSurfaceTileComposer.FromContentGroup", source, StringComparison.Ordinal);
        Assert.Contains("shelf.DistinctTitleCount >= 2", source, StringComparison.Ordinal);
        Assert.Contains("\"Books\" or \"Movies\" or \"Audiobooks\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Books\" or \"Comics\" or \"Movies\" or \"Albums\"", source, StringComparison.Ordinal);
        Assert.Contains("<PeopleCatalogList", source, StringComparison.Ordinal);
        Assert.Contains("PeoplePageSize = 100", source, StringComparison.Ordinal);
        Assert.Contains("Load more people", source, StringComparison.Ordinal);
        Assert.Contains("<BrowseShellStyles", source, StringComparison.Ordinal);
        Assert.Contains("<BrowseShellStyles", browseShellSource, StringComparison.Ordinal);
        Assert.Contains(".browse-shell__grid", browseShellStylesSource, StringComparison.Ordinal);
        Assert.Contains("display: flex", browseShellStylesSource, StringComparison.Ordinal);
        Assert.Contains("flex-wrap: wrap", browseShellStylesSource, StringComparison.Ordinal);
        Assert.Contains("browse-shell collections-browse", source, StringComparison.Ordinal);
        Assert.Contains("browse-shell__filter-surface collections-filter-surface", source, StringComparison.Ordinal);
        Assert.Contains("browse-shell__filter-search-row", source, StringComparison.Ordinal);
        Assert.Contains("browse-shell__control-label\">Filter by", source, StringComparison.Ordinal);
        Assert.Contains("browse-shell__display-label\">Display", source, StringComparison.Ordinal);
        Assert.DoesNotContain("collections-overview__intro", source, StringComparison.Ordinal);
        Assert.Contains("@if (CanManageCuratedCollections)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<CinematicHeroCarousel", source, StringComparison.Ordinal);
        Assert.Contains("<MediaTileGrid", source, StringComparison.Ordinal);
        Assert.Contains("MediaTileArtworkResolver.Resolve(", composerSource, StringComparison.Ordinal);
        Assert.Contains("preferLandscapeTile: true", composerSource, StringComparison.Ordinal);
        Assert.Contains("Shape = surface.Shape", composerSource, StringComparison.Ordinal);
        Assert.Contains("SurfaceKind = surface.SurfaceKind", composerSource, StringComparison.Ordinal);
        Assert.Contains("UseLandscapeGroupTile = true", composerSource, StringComparison.Ordinal);
        Assert.Contains("item.RenderAsLandscapeGroupTile", tileGridSource, StringComparison.Ordinal);
        Assert.Contains("<MediaGroupTile", tileGridSource, StringComparison.Ordinal);
        Assert.Contains("media-group-tile__artwork", groupTileSource, StringComparison.Ordinal);
        Assert.Contains("MediaArtworkGroupPreviewLayout.Cluster", groupTileSource, StringComparison.Ordinal);
        Assert.DoesNotContain("MediaArtworkGroupPreviewLayout.Mosaic", groupTileSource, StringComparison.Ordinal);
        Assert.Contains("media-group-tile__identity", groupTileSource, StringComparison.Ordinal);
        Assert.Contains("media-group-tile__year", groupTileSource, StringComparison.Ordinal);
        Assert.Contains("media-group-tile__media-count", groupTileSource, StringComparison.Ordinal);
        Assert.DoesNotContain("media-group-tile__kind", groupTileSource, StringComparison.Ordinal);
        Assert.DoesNotContain("At a glance", groupTileSource, StringComparison.Ordinal);
        Assert.DoesNotContain("HighlightedItems", groupTileSource, StringComparison.Ordinal);
        Assert.DoesNotContain("media-group-tile__highlights", groupTileSource, StringComparison.Ordinal);
        Assert.DoesNotContain("MediaArtworkCarousel", groupTileSource, StringComparison.Ordinal);
        Assert.Contains("\"Issues\" => \"issue\"", groupTileSource, StringComparison.Ordinal);
        Assert.Contains("\"Episodes\" => \"episode\"", groupTileSource, StringComparison.Ordinal);
        Assert.Contains("\"Tracks\" => \"track\"", groupTileSource, StringComparison.Ordinal);
        Assert.Contains("--media-group-tile-width: clamp(560px, 40vw, 740px)", groupTileStylesSource, StringComparison.Ordinal);
        Assert.Contains("--media-group-tile-height: clamp(300px, 20vw, 365px)", groupTileStylesSource, StringComparison.Ordinal);
        Assert.Contains("PreviewTotalCount = collection.ItemCount", composerSource, StringComparison.Ordinal);
        Assert.Contains("TileTextMode = MediaTileTextMode.CoverOnly", composerSource, StringComparison.Ordinal);
        Assert.Contains("Take(4)", composerSource, StringComparison.Ordinal);
        Assert.Contains("browse-shell__search", source, StringComparison.Ordinal);
        Assert.Contains("browse-shell__sort", source, StringComparison.Ordinal);
        Assert.Contains("Search collections", source, StringComparison.Ordinal);
        Assert.Contains("Search shelves", source, StringComparison.Ordinal);
        Assert.Contains("All media", source, StringComparison.Ordinal);
        Assert.Contains("ShelfLanePreviews", source, StringComparison.Ordinal);
        Assert.Contains("Book series in your library", source, StringComparison.Ordinal);
        Assert.Contains("Movie series in your library", source, StringComparison.Ordinal);
        Assert.Contains("Audiobook series in your library", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Book series and comic volumes in your library", source, StringComparison.Ordinal);
        Assert.DoesNotContain("All shelf types", source, StringComparison.Ordinal);
        Assert.Contains("Search people or roles", source, StringComparison.Ordinal);
        Assert.Contains("Recently Updated", configurationSource, StringComparison.Ordinal);
        Assert.Contains("Item count", source, StringComparison.Ordinal);
        Assert.Contains("Broader franchise, universe, and related-work rollups", source, StringComparison.Ordinal);
        Assert.Contains("MediaKind = \"Collection\"", composerSource, StringComparison.Ordinal);
        Assert.Contains("PaletteArtwork(collection)", composerSource, StringComparison.Ordinal);
        Assert.Contains("ArtworkPalette = collection.ArtworkPalette", composerSource, StringComparison.Ordinal);
        Assert.Contains("SecondaryAccentColor = secondaryAccentColor", composerSource, StringComparison.Ordinal);
        Assert.Contains("ArtworkStackItems = artworkStackItems", composerSource, StringComparison.Ordinal);
        Assert.Contains("ToArtworkShape(item.ArtworkShape, item.MediaType)", composerSource, StringComparison.Ordinal);
        Assert.Contains("\"Watch\", collection.WatchCount", composerSource, StringComparison.Ordinal);
        Assert.Contains("\"Listen\", collection.ListenCount", composerSource, StringComparison.Ordinal);
        Assert.Contains("\"Read\", collection.ReadCount", composerSource, StringComparison.Ordinal);
        Assert.Contains("\"Movies\", collection.MovieCount", composerSource, StringComparison.Ordinal);
        Assert.Contains("\"TV\", collection.TvCount", composerSource, StringComparison.Ordinal);
        Assert.Contains("EarliestYear = collection.EarliestYear", composerSource, StringComparison.Ordinal);
        Assert.Contains("LatestYear = collection.LatestYear", composerSource, StringComparison.Ordinal);
        Assert.Contains("var navigationUrl = collection.Person is null", composerSource, StringComparison.Ordinal);
        Assert.Contains("$\"/details/person/{collection.Person.Id:D}\"", composerSource, StringComparison.Ordinal);
        Assert.Contains("PrimaryNavigationUrl = navigationUrl", composerSource, StringComparison.Ordinal);
        Assert.Contains("Person = collection.Person is null", composerSource, StringComparison.Ordinal);
        Assert.Contains("media-group-tile__person", groupTileSource, StringComparison.Ordinal);
        Assert.Contains("GroupActionNoun => HasPerson ? \"Person\"", groupTileSource, StringComparison.Ordinal);
        Assert.Contains(".media-group-tile.has-person .media-group-tile__identity", groupTileStylesSource, StringComparison.Ordinal);
        Assert.Contains("people-catalog-list", peopleListSource, StringComparison.Ordinal);
        Assert.Contains("/details/person/", peopleListSource, StringComparison.Ordinal);
        Assert.Contains("Read", peopleListSource, StringComparison.Ordinal);
        Assert.Contains("Watch", peopleListSource, StringComparison.Ordinal);
        Assert.Contains("Listen", peopleListSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Playlist", source, StringComparison.Ordinal);
        Assert.DoesNotContain("READ COLLECTIONS", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LISTEN COLLECTIONS", source, StringComparison.Ordinal);
        Assert.DoesNotContain("WATCH COLLECTIONS", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CROSS-MEDIA COLLECTIONS", source, StringComparison.Ordinal);
        Assert.DoesNotContain("collections-hub__tabs", source, StringComparison.Ordinal);
        Assert.DoesNotContain("collections-hub-tab", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GLOBAL COLLECTIONS", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SYSTEM COLLECTIONS", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CREATED BY YOU", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<CollectionInlineInspector", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<CollectionHubSection", source, StringComparison.Ordinal);
        Assert.DoesNotContain("collections-table-wrap", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<MudTable", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CollectionsHub_RemovesOldLaneComponentsAndCss()
    {
        var collectionsPath = GetRepoFilePath(@"src\MediaEngine.Web\Components\Collections");
        var removedFiles = new[]
        {
            "CollectionsPage.razor.css",
            "CollectionHubSection.razor",
            "CollectionHubSection.razor.css",
            "CollectionHubCard.razor",
            "CollectionHubCard.razor.css",
            "CollectionHubStats.razor",
            "CollectionHubStats.razor.css",
            "CollectionArtworkStack.razor",
            "CollectionArtworkStack.razor.css",
            "CollectionInlineInspector.razor",
            "CollectionInlineInspector.razor.css",
            "CollectionSectionLabel.razor",
            "CollectionSectionLabel.razor.css",
        };

        foreach (var file in removedFiles)
        {
            Assert.False(File.Exists(Path.Combine(collectionsPath, file)), $"{file} should stay removed.");
        }
    }

    [Fact]
    public void CollectionDetail_UsesCanonicalSharedDetailSurface()
    {
        var pagesPath = Path.GetDirectoryName(
            GetRepoFilePath(@"src\MediaEngine.Web\Components\Pages\UnifiedDetailPage.razor"))!;
        var route = File.ReadAllText(Path.Combine(pagesPath, "UnifiedDetailPage.razor"));
        var detail = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Details\DetailPage.razor"));
        var composer = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Services\MediaTiles\CollectionSurfaceTileComposer.cs"));

        Assert.False(File.Exists(Path.Combine(pagesPath, "CollectionDetail.razor")));
        Assert.False(File.Exists(Path.Combine(pagesPath, "CollectionDetail.razor.css")));
        Assert.Contains("@page \"/details/{EntityType}/{Id:guid}\"", route, StringComparison.Ordinal);
        Assert.Contains("/details/collection/", composer, StringComparison.Ordinal);
        Assert.Contains("CollectionEditorLauncher.OpenAsync(new CollectionEditorLaunchRequest", detail, StringComparison.Ordinal);
        Assert.Contains("ActiveProfileId = activeProfile?.Id ?? collection.ProfileId", detail, StringComparison.Ordinal);
        Assert.Contains("<DetailPrimaryModule Model=\"Model\"", detail, StringComparison.Ordinal);
        Assert.Contains("<DetailTabs Tabs=\"VisibleTabs\"", detail, StringComparison.Ordinal);
    }

    [Fact]
    public void StructuralShelfComposer_UsesSeriesIdentityCountAndDetailRoute()
    {
        var collectionId = Guid.NewGuid();
        var tile = CollectionSurfaceTileComposer.FromContentGroup(new ContentGroupViewModel
        {
            CollectionId = collectionId,
            DisplayName = "The Dark Knight Collection",
            PrimaryMediaType = "Movies",
            WorkCount = 3,
            DistinctTitleCount = 3,
            EarliestYear = 2005,
            LatestYear = 2012,
            PreviewItems =
            [
                new ContentGroupPreviewItemDto(Guid.NewGuid(), "Batman Begins", "/art/batman-begins.jpg", "portrait", "1"),
                new ContentGroupPreviewItemDto(Guid.NewGuid(), "The Dark Knight", "/art/dark-knight.jpg", "portrait", "2"),
            ],
        });

        Assert.Equal("The Dark Knight", tile.Title);
        Assert.Equal(3, tile.PreviewTotalCount);
        Assert.Equal(MediaTilePresentation.MovieSeries, tile.Presentation);
        Assert.Equal($"/details/movieseries/{collectionId:D}?context=watch", tile.DetailsNavigationUrl);
        Assert.Null(tile.Person);
    }

    [Fact]
    public void CollectionEditor_UsesTypedTextareaForDescription()
    {
        var source = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Collections\CollectionEditorShell.razor"));
        var styles = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Collections\CollectionEditorShell.razor.css"));

        Assert.Contains("<AppTextarea Value=\"@_description\"", source, StringComparison.Ordinal);
        Assert.Contains("Value=\"@_name\"", source, StringComparison.Ordinal);
        Assert.Contains("Curated collection", source, StringComparison.Ordinal);
        Assert.Contains("Cross-media", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Label=\"Enabled\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Label=\"Visibility\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Value=\"rule.", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<AppTextField T=\"string\"\r\n                          Value=\"_description\"", source, StringComparison.Ordinal);
        Assert.Contains("min-height: 172px !important", styles, StringComparison.Ordinal);
        Assert.Contains("EditingCollection.CollectionType is \"Custom\" or \"Playlist\" or \"Smart\"", source, StringComparison.Ordinal);
        Assert.Contains("SetMembershipModeAsync", source, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Membership mode\"", source, StringComparison.Ordinal);
        Assert.Contains("Add at least one complete rule before saving a dynamic collection.", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Discovery & placement", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Label=\"Featured\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_isFeatured", source, StringComparison.Ordinal);
        Assert.Contains(">Delete collection</AppButton>", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CollectionCreation_UsesCollectionsOnlyThreeStepWizardAndFullEditorMembership()
    {
        var wizard = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Collections\CollectionWizard.razor"));
        var css = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Collections\CollectionWizard.razor.css"));
        var launcher = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Services\Editing\CollectionEditorLauncherService.cs"));
        var request = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Services\Editing\CollectionEditorModels.cs"));
        var pickerPath = GetRepoFilePath(@"src\MediaEngine.Web\Components\Discovery\AddToCollectionDialog.razor");
        var pickerCssPath = GetRepoFilePath(@"src\MediaEngine.Web\Components\Discovery\AddToCollectionDialog.razor.css");
        var editor = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Collections\CollectionEditorShell.razor"));
        var ruleBuilder = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Collections\CollectionRuleBuilder.razor"));
        var sharedRuleBuilder = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Rules\SharedRuleBuilder.razor"));
        var collectionCatalog = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Rules\CollectionRuleCatalog.cs"));
        var ruleConfiguration = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Rules\RuleBuilderConfiguration.cs"));
        var collectionsPage = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Collections\CollectionsPage.razor"));
        var sectionConfiguration = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Collections\CollectionsSectionConfiguration.cs"));

        Assert.Contains("Step @VisibleStepOrdinal(_step) of @VisibleSteps.Count", wizard, StringComparison.Ordinal);
        Assert.Contains("VisibleSteps => [1, 2, 3]", wizard, StringComparison.Ordinal);
        Assert.Contains("<MediaEditorSurface>", wizard, StringComparison.Ordinal);
        Assert.Contains("collection-setup-workspace", wizard, StringComparison.Ordinal);
        Assert.DoesNotContain("<AppDialogShell", wizard, StringComparison.Ordinal);
        Assert.DoesNotContain("TypeSelectionConfirmed", request, StringComparison.Ordinal);
        Assert.DoesNotContain("TriggeringWork", request, StringComparison.Ordinal);
        Assert.False(File.Exists(pickerPath), "The media-detail Add to Collection dialog must stay removed.");
        Assert.False(File.Exists(pickerCssPath), "The removed Add to Collection dialog must not retain stale CSS.");
        Assert.Contains("is-three-step", css, StringComparison.Ordinal);
        Assert.Contains("How should this", wizard, StringComparison.Ordinal);
        Assert.Contains("<CollectionRuleBuilder", wizard, StringComparison.Ordinal);
        Assert.Contains("<CollectionRuleBuilder", editor, StringComparison.Ordinal);
        Assert.Contains("CollectionRuleCatalog.Instance", ruleBuilder, StringComparison.Ordinal);
        Assert.Contains("Build rules", collectionCatalog, StringComparison.Ordinal);
        Assert.DoesNotContain("Live matches", sharedRuleBuilder, StringComparison.Ordinal);
        Assert.Contains("Sort results", sharedRuleBuilder, StringComparison.Ordinal);
        Assert.Contains("Add group", sharedRuleBuilder, StringComparison.Ordinal);
        Assert.Contains("Add condition", sharedRuleBuilder, StringComparison.Ordinal);
        Assert.Contains("Search rule types", sharedRuleBuilder, StringComparison.Ordinal);
        Assert.Contains("::deep .collection-rule-builder__picker-option", File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Rules\SharedRuleBuilder.razor.css")), StringComparison.Ordinal);
        Assert.Contains("SecondarySortField", ruleBuilder, StringComparison.Ordinal);
        Assert.Contains("<CollectionRuleValuePicker", sharedRuleBuilder, StringComparison.Ordinal);
        Assert.Contains("is any of", ruleConfiguration, StringComparison.Ordinal);
        var valuePicker = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Collections\CollectionRuleValuePicker.razor"));
        Assert.Contains("Select all shown", valuePicker, StringComparison.Ordinal);
        Assert.Contains("CheckBox", valuePicker, StringComparison.Ordinal);
        Assert.Contains("GetCollectionEntityFieldValuesAsync", valuePicker, StringComparison.Ordinal);
        Assert.Contains("SortFieldChanged", ruleBuilder, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectRuleCategory", wizard, StringComparison.Ordinal);
        Assert.DoesNotContain("RunPreviewAsync", wizard, StringComparison.Ordinal);
        Assert.Contains("CreateCollectionWithItemsAsync", wizard, StringComparison.Ordinal);
        Assert.Contains("Fine-tune it in the full editor", wizard, StringComparison.Ordinal);
        Assert.Contains("_selectedManualItems.Count > 0", wizard, StringComparison.Ordinal);
        Assert.DoesNotContain("collection-artwork-file", wizard, StringComparison.Ordinal);
        Assert.DoesNotContain("VisibilityLabel", wizard, StringComparison.Ordinal);
        Assert.Contains("LookupCollectionMediaAsync", wizard, StringComparison.Ordinal);
        Assert.Contains("ManualCategories", wizard, StringComparison.Ordinal);
        Assert.Contains("Choose items from your library", wizard, StringComparison.Ordinal);
        Assert.Contains("Search titles, creators, series, or shows", wizard, StringComparison.Ordinal);
        Assert.Contains("_selectedManualItems.Keys.ToList()", wizard, StringComparison.Ordinal);
        Assert.DoesNotContain("Remove included item", wizard, StringComparison.Ordinal);
        Assert.DoesNotContain("collection-wizard__type-card", css, StringComparison.Ordinal);
        Assert.DoesNotContain("collection-wizard__origin", css, StringComparison.Ordinal);
        Assert.DoesNotContain("collection-wizard__included-list", css, StringComparison.Ordinal);
        Assert.Contains("PersistenceVisibility", editor, StringComparison.Ordinal);
        Assert.Contains("RenderItemsTab", editor, StringComparison.Ordinal);
        Assert.Contains("MediaTileArtworkUrl.Sized(coverUrl, \"s\")", editor, StringComparison.Ordinal);
        Assert.Contains("collection-editor-item-art--placeholder", editor, StringComparison.Ordinal);
        Assert.Contains("FormatMatchSummary(_previewResult)", editor, StringComparison.Ordinal);
        Assert.Contains("Variant=\"Variant.Filled\" Color=\"Color.Error\"", editor, StringComparison.Ordinal);
        Assert.Contains("OpenItemPickerAsync", editor, StringComparison.Ordinal);
        Assert.Contains("item(s) in this collection", editor, StringComparison.Ordinal);
        Assert.DoesNotContain("collection-editor-column-title\">Available", editor, StringComparison.Ordinal);
        Assert.Contains("RuleValueProviderKind.CollectionLibrary", collectionCatalog, StringComparison.Ordinal);
        Assert.Contains("GetCollectionFieldValuesAsync", valuePicker, StringComparison.Ordinal);
        Assert.Contains("collection-editor-workspace", editor, StringComparison.Ordinal);
        Assert.Contains("sme-section-nav collection-editor-rail", editor, StringComparison.Ordinal);
        Assert.Contains("RenderArtworkTab", editor, StringComparison.Ordinal);
        Assert.Contains("(\"artwork\", \"poster\", \"Poster / Cover\"", editor, StringComparison.Ordinal);
        Assert.Contains("(\"artwork-background\", \"background\", \"Background\"", editor, StringComparison.Ordinal);
        Assert.DoesNotContain("artwork-banner", editor, StringComparison.Ordinal);
        Assert.Contains("(\"artwork-logo\", \"logo\", \"Logo\"", editor, StringComparison.Ordinal);
        Assert.Contains("sme-section-nav__nested", editor, StringComparison.Ordinal);
        Assert.Contains("RenderHistoryTab", editor, StringComparison.Ordinal);
        Assert.DoesNotContain("<AppDialogShell", editor, StringComparison.Ordinal);
        Assert.DoesNotContain("collection-editor-tabs", editor, StringComparison.Ordinal);
        Assert.DoesNotContain("collection-editor-section-title\">Publication", editor, StringComparison.Ordinal);
        Assert.DoesNotContain("Label=\"Publication\"", collectionsPage, StringComparison.Ordinal);
        Assert.DoesNotContain("status=published", sectionConfiguration, StringComparison.Ordinal);
        Assert.Contains("var(--tl-accent-collection)", css, StringComparison.Ordinal);
        var sharedDialogCss = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Shared\AppDialogShell.razor.css"));
        Assert.Contains("100dvh", sharedDialogCss, StringComparison.Ordinal);
        Assert.Contains("grid-column: 2", sharedDialogCss, StringComparison.Ordinal);
        Assert.Contains("OpenGuidedSetupAsync", launcher, StringComparison.Ordinal);
        Assert.Contains("MaxWidth.ExtraLarge", launcher, StringComparison.Ordinal);
        Assert.Contains("FullWidth = isCollectionEditor", launcher, StringComparison.Ordinal);
        Assert.Contains("request.Mode == CollectionEditorMode.CuratedCollection ? MaxWidth.ExtraLarge", launcher, StringComparison.Ordinal);
        Assert.Contains("FullWidth = request.Mode == CollectionEditorMode.CuratedCollection", launcher, StringComparison.Ordinal);
    }

    [Fact]
    public void TuvimaArtworkStack_IsGenericSeededAndShapeAware()
    {
        var source = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Shared\TuvimaArtworkStack.razor"));
        var styles = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Shared\TuvimaArtworkStack.razor.css"));
        var modelSource = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Models\ViewDTOs\ArtworkStackModels.cs"));

        Assert.Contains("public sealed class ArtworkStackItem", modelSource, StringComparison.Ordinal);
        Assert.Contains("public enum ArtworkShape", modelSource, StringComparison.Ordinal);
        Assert.Contains("public enum ArtworkStackVariant", modelSource, StringComparison.Ordinal);
        Assert.Contains("[Parameter] public IReadOnlyList<ArtworkStackItem> Items", source, StringComparison.Ordinal);
        Assert.Contains("[Parameter] public string Seed", source, StringComparison.Ordinal);
        Assert.Contains("[Parameter] public MediaEngine.Domain.Models.ArtworkPalette? Palette", source, StringComparison.Ordinal);
        Assert.Contains("Palette?.CssVariableStyle", source, StringComparison.Ordinal);
        Assert.Contains("OrderBy(item => StableHash", source, StringComparison.Ordinal);
        Assert.Contains("data-shape=\"@ShapeValue(slot.item.Shape)\"", source, StringComparison.Ordinal);
        Assert.Contains("--artwork-ratio: 1 / 1", styles, StringComparison.Ordinal);
        Assert.Contains("--artwork-ratio: 2 / 3", styles, StringComparison.Ordinal);
        Assert.Contains("--artwork-ratio: 16 / 9", styles, StringComparison.Ordinal);
        Assert.Contains("--left", source, StringComparison.Ordinal);
        Assert.Contains("--top", source, StringComparison.Ordinal);
        Assert.Contains("translate(-50%, -50%)", styles, StringComparison.Ordinal);
        Assert.Contains("calc(var(--left) + 18%)", styles, StringComparison.Ordinal);
        Assert.Contains("width: calc(var(--artwork-width) * 1.72)", styles, StringComparison.Ordinal);
        Assert.Contains("top: calc(var(--top) - 5%)", styles, StringComparison.Ordinal);
        Assert.Contains("min-height: clamp(46rem, 84vh, 64rem)", styles, StringComparison.Ordinal);
        Assert.Contains("overflow: visible", styles, StringComparison.Ordinal);
        Assert.Contains(".artwork-stack--hero .artwork-stack__stage", styles, StringComparison.Ordinal);
        Assert.Contains("background: transparent", styles, StringComparison.Ordinal);
        Assert.Contains("artwork-stack--all-square", source, StringComparison.Ordinal);
        Assert.Contains("artwork-stack--all-portrait", source, StringComparison.Ordinal);
        Assert.Contains("artwork-stack--mixed", source, StringComparison.Ordinal);
        Assert.Contains("nth-of-type(n + 4)", styles, StringComparison.Ordinal);
        Assert.DoesNotContain("<style", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("FeaturedCover", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PrimaryCover", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BestCover", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Collection", modelSource, StringComparison.Ordinal);
    }

    private static string GetRepoFilePath(string relativePath) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relativePath));
}
