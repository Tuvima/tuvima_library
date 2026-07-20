namespace MediaEngine.Web.Tests;

public sealed class Phase5InlineEditingTests
{
    [Fact]
    public void DetailPage_EditActionLaunchesSharedEditor()
    {
        var source = ReadSource("src/MediaEngine.Web/Components/Details/DetailPage.razor");

        Assert.Contains("MediaEditorLauncherService MediaEditorLauncher", source, StringComparison.Ordinal);
        Assert.Contains("action.Key is \"edit-media\" or \"edit\"", source, StringComparison.Ordinal);
        Assert.Contains("MediaEditorLauncher.BeginInline(request)", source, StringComparison.Ordinal);
        Assert.Contains("<SharedMediaEditorShell", source, StringComparison.Ordinal);
        Assert.Contains("Inline=\"true\"", source, StringComparison.Ordinal);
        Assert.Contains("HeroConstrained=\"true\"", source, StringComparison.Ordinal);
        Assert.Contains("IncludeHero=\"false\"", source, StringComparison.Ordinal);
        Assert.Contains("ActiveProfileId = activeProfile?.Id", source, StringComparison.Ordinal);
        Assert.Contains("Mode = SharedMediaEditorMode.Normal", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DetailPage_OnlyFlipsTheHeroAndKeepsLowerDetailContentMounted()
    {
        var source = ReadSource("src/MediaEngine.Web/Components/Details/DetailPage.razor");
        var stageIndex = source.IndexOf("tl-detail-edit-stage", StringComparison.Ordinal);
        var editorIndex = source.IndexOf("HeroConstrained=\"true\"", stageIndex, StringComparison.Ordinal);
        var stageEndIndex = source.IndexOf("@if (IsAudioDetail)", editorIndex, StringComparison.Ordinal);
        var tabsIndex = source.IndexOf("<DetailTabs Tabs=\"VisibleTabs\"", stageEndIndex, StringComparison.Ordinal);

        Assert.True(stageIndex >= 0);
        Assert.True(editorIndex > stageIndex);
        Assert.True(stageEndIndex > editorIndex);
        Assert.True(tabsIndex > stageEndIndex);
    }

    [Fact]
    public void BrowseRowEditActionUsesSharedEditorWhileCardsOpenDetails()
    {
        var browse = ReadSource("src/MediaEngine.Web/Components/Browse/MediaBrowseShell.razor");
        var table = ReadSource("src/MediaEngine.Web/Components/Library/LibraryConfigurableTable.razor");
        var card = ReadSource("src/MediaEngine.Web/Components/MediaTiles/MediaTile.razor");

        Assert.Contains("MediaEditorLauncherService MediaEditorLauncher", browse, StringComparison.Ordinal);
        Assert.Contains("OnEditClicked=\"OpenItemEditorAsync\"", browse, StringComparison.Ordinal);
        Assert.DoesNotContain("OnEditClicked=\"OpenCardEditorAsync\"", browse, StringComparison.Ordinal);
        Assert.DoesNotContain("MediaGroupPage", browse, StringComparison.Ordinal);
        Assert.Contains("MediaEditorLauncher.OpenAsync(new MediaEditorLaunchRequest", browse, StringComparison.Ordinal);
        Assert.Contains("OnEditClicked.InvokeAsync(item.EntityId)", table, StringComparison.Ordinal);
        Assert.Contains("href=\"@DetailsNavigationUrl\"", card, StringComparison.Ordinal);
        Assert.DoesNotContain("OnEditClicked", card, StringComparison.Ordinal);
    }

    [Fact]
    public void SearchResult_EditActionLaunchesSharedEditorAndRefreshesSearch()
    {
        var source = ReadSource("src/MediaEngine.Web/Components/Pages/SearchPage.razor");

        Assert.Contains("MediaEditorLauncherService MediaEditorLauncher", source, StringComparison.Ordinal);
        Assert.Contains("OpenSearchResultEditorAsync", source, StringComparison.Ordinal);
        Assert.Contains("Mode = SharedMediaEditorMode.Normal", source, StringComparison.Ordinal);
        Assert.Contains("await SearchAsync();", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ReviewQueue_ReviewActionLaunchesEditorInReviewModeWithReviewId()
    {
        var source = ReadSource("src/MediaEngine.Web/Components/Settings/SettingsReviewQueueTab.razor");

        Assert.Contains("Mode = SharedMediaEditorMode.Review", source, StringComparison.Ordinal);
        Assert.Contains("ReviewItemId = item.Id", source, StringComparison.Ordinal);
        Assert.Contains("await LoadAsync();", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedEditor_ReviewResolutionIsExplicitAndUsesEngineApi()
    {
        var shell = ReadSource("src/MediaEngine.Web/Components/MediaEditor/SharedMediaEditorShell.razor");
        var code = ReadSource("src/MediaEngine.Web/Components/MediaEditor/SharedMediaEditorShell.razor.cs");

        Assert.Contains("GetDirtySaveLabel", shell, StringComparison.Ordinal);
        Assert.Contains("GetResolveReviewLabel", shell, StringComparison.Ordinal);
        Assert.Contains("ResolveReviewWithoutChangesAsync", code, StringComparison.Ordinal);
        Assert.Contains("Orchestrator.ResolveReviewAsync", code, StringComparison.Ordinal);
        Assert.Contains("Review was not resolved because changes could not be saved.", code, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedEditor_DetailsTabUsesInlineMetadataOverrideLayout()
    {
        var shell = ReadSource("src/MediaEngine.Web/Components/MediaEditor/SharedMediaEditorShell.razor");

        Assert.Contains("Title=\"Appearance\"", shell, StringComparison.Ordinal);
        Assert.Contains("Title=\"My Library\"", shell, StringComparison.Ordinal);
        Assert.Contains("Title=\"Source facts\"", shell, StringComparison.Ordinal);
        Assert.Contains("sme-details-grid", shell, StringComparison.Ordinal);
        Assert.Contains("Title=\"Artwork preview\"", shell, StringComparison.Ordinal);
        Assert.Contains("sme-details-grid__artwork", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("sme-sidebar-artwork", shell, StringComparison.Ordinal);
        Assert.Contains("@if (!Inline)", shell, StringComparison.Ordinal);
        Assert.Contains("ActionIcon=\"@GetInlineFieldActionIcon(field.Key)\"", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("Match Information", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("Title=\"Local Library Details\"", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("Title=\"Field Rules\"", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("Title=\"Review Focus\"", shell, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedFieldRow_ProvidesSideActionForLockedCanonicalOverrides()
    {
        var source = ReadSource("src/MediaEngine.Web/Components/Shared/AppFormFieldRow.razor");

        Assert.Contains("ActionIcon", source, StringComparison.Ordinal);
        Assert.Contains("MudIconButton", source, StringComparison.Ordinal);
        Assert.Contains("OnAction.InvokeAsync()", source, StringComparison.Ordinal);
        Assert.Contains("ConfirmingAction", source, StringComparison.Ordinal);
        Assert.Contains("OnConfirmAction.InvokeAsync()", source, StringComparison.Ordinal);
        Assert.Contains("OnCancelAction.InvokeAsync()", source, StringComparison.Ordinal);
        Assert.Contains("Color=\"Color.Success\"", source, StringComparison.Ordinal);
        Assert.Contains("Color=\"Color.Error\"", source, StringComparison.Ordinal);
        Assert.Contains("Unlocked", source, StringComparison.Ordinal);
        var styles = ReadSource("src/MediaEngine.Web/Components/Shared/AppFormFieldRow.razor.css");
        Assert.Contains("flex-direction: row", styles, StringComparison.Ordinal);
        Assert.Contains("tl-field-grid--confirming", styles, StringComparison.Ordinal);
        Assert.Contains("tl-field-confirm-button--accept", styles, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedEditor_InlineCanonicalOverridesSaveThroughDisplayOverrides()
    {
        var code = ReadSource("src/MediaEngine.Web/Components/MediaEditor/SharedMediaEditorShell.razor.cs");

        Assert.Contains("ShouldSaveAsDisplayOverride(scopedKey, key)", code, StringComparison.Ordinal);
        Assert.Contains("ShouldSaveAsDisplayOverride(entry.RawKey, entry.Key.Key)", code, StringComparison.Ordinal);
        Assert.Contains("SaveItemEditorPreferencesAsync", code, StringComparison.Ordinal);
        Assert.Contains("SaveItemDisplayOverridesAsync(scopeGroup.Key.EntityId, overrideFields)", code, StringComparison.Ordinal);
        Assert.Contains("SaveItemPreferencesAsync(scopeGroup.Key.EntityId, preferenceFields)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedEditor_MatchInformationUsesProviderNameAndOmitsTechnicalRows()
    {
        var code = ReadSource("src/MediaEngine.Web/Components/MediaEditor/SharedMediaEditorShell.razor.cs");

        Assert.Contains("GetRetailMatchDisplayName(summary)", code, StringComparison.Ordinal);
        Assert.Contains("(\"Retail Match\", !string.IsNullOrWhiteSpace(provider) ? provider : hasProviderEvidence ? \"Retail matched\" : \"Not linked\")", code, StringComparison.Ordinal);
        Assert.Contains("(\"Provider ID\"", code, StringComparison.Ordinal);
        Assert.DoesNotContain("(\"Match Source\"", code, StringComparison.Ordinal);
        Assert.DoesNotContain("NormalizeRetailProviderLabel(summary?.MatchSource)", code, StringComparison.Ordinal);
        Assert.Contains("Guid.TryParse(trimmed, out _)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ProviderNameFromBridgeIdentifier", code, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedEditor_InlineModeKeepsDiscardAndStructuralConfirmationsVisible()
    {
        var shell = ReadSource("src/MediaEngine.Web/Components/MediaEditor/SharedMediaEditorShell.razor");

        Assert.Contains("Inline && _confirmDiscard", shell, StringComparison.Ordinal);
        Assert.Contains("Discard unsaved changes?", shell, StringComparison.Ordinal);
        Assert.Contains("Inline && _pendingMembershipPreview is not null", shell, StringComparison.Ordinal);
        Assert.Contains("Save and Move", shell, StringComparison.Ordinal);
        Assert.Contains("Title=\"Parent & position\"", shell, StringComparison.Ordinal);
        Assert.Contains("Search outside this parent", shell, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedEditor_AudiobooksUseFocusedChapterContentsAndFileDerivedBoundaries()
    {
        var shell = ReadSource("src/MediaEngine.Web/Components/MediaEditor/SharedMediaEditorShell.razor");
        var code = ReadSource("src/MediaEngine.Web/Components/MediaEditor/SharedMediaEditorShell.razor.cs");
        var metadata = ReadSource("src/MediaEngine.Api/Endpoints/MetadataEndpoints.cs");

        Assert.Contains("else if (_activeTab == \"contents\")", shell, StringComparison.Ordinal);
        Assert.Contains("Rename chapter labels without changing file-derived timing or boundaries", shell, StringComparison.Ordinal);
        Assert.Contains("QueueAudiobookChapterReset", shell, StringComparison.Ordinal);
        Assert.Contains("UpsertAudiobookChapterTitleOverrideAsync", code, StringComparison.Ordinal);
        Assert.Contains("DeleteAudiobookChapterTitleOverrideAsync", code, StringComparison.Ordinal);
        Assert.Contains("if (normalized == \"Audiobooks\")", metadata, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedEditor_ContainerFilesAreAnAggregateReconciliationSummary()
    {
        var shell = ReadSource("src/MediaEngine.Web/Components/MediaEditor/SharedMediaEditorShell.razor");
        var code = ReadSource("src/MediaEngine.Web/Components/MediaEditor/SharedMediaEditorShell.razor.cs");

        Assert.Contains("Title=\"Attached files\"", shell, StringComparison.Ordinal);
        Assert.Contains("A reconciliation summary across the owned children", shell, StringComparison.Ordinal);
        Assert.Contains("ContainerAttachedFileCount", code, StringComparison.Ordinal);
        Assert.Contains("ContainerMissingFileCount", code, StringComparison.Ordinal);
        Assert.Contains("Open a child from Contents", shell, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedEditor_MatchIdentityTabUsesStructuredRetailAndWikidataWorkflow()
    {
        var shell = ReadSource("src/MediaEngine.Web/Components/MediaEditor/SharedMediaEditorShell.razor");
        var code = ReadSource("src/MediaEngine.Web/Components/MediaEditor/SharedMediaEditorShell.razor.cs");
        var styles = ReadSource("src/MediaEngine.Web/Components/MediaEditor/SharedMediaEditorShell.razor.css");

        Assert.Contains("sme-match-status-strip", shell, StringComparison.Ordinal);
        Assert.Contains("sme-match-current-panel", shell, StringComparison.Ordinal);
        Assert.Contains("sme-match-search-panel", shell, StringComparison.Ordinal);
        Assert.Contains("Current Identity", shell, StringComparison.Ordinal);
        Assert.Contains("sme-match-search-tabs", shell, StringComparison.Ordinal);
        Assert.Contains("Text=\"Retail\"", shell, StringComparison.Ordinal);
        Assert.Contains("Text=\"Wikidata\"", shell, StringComparison.Ordinal);
        Assert.Contains("sme-header-actions", shell, StringComparison.Ordinal);
        Assert.Contains("Change Type", shell, StringComparison.Ordinal);
        Assert.Contains("<AppMediaTypeSelect Value=\"@_selectedMediaType\"", shell, StringComparison.Ordinal);
        Assert.Contains("ValueChanged=\"OnSelectedMediaTypeChanged\"", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("sme-match-type-select", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("sme-search-targets", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("Keep Match", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("Keep QID", shell, StringComparison.Ordinal);
        Assert.Contains("Clear QID", shell, StringComparison.Ordinal);
        Assert.Contains("Use This Match", shell, StringComparison.Ordinal);
        Assert.Contains("Use This QID", shell, StringComparison.Ordinal);
        Assert.Contains("sme-current-identity-links", shell, StringComparison.Ordinal);
        Assert.Contains("currentRetail.Links", shell, StringComparison.Ordinal);
        Assert.Contains("currentQid.Links", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("currentRetail.Description", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("currentQid.Description", shell, StringComparison.Ordinal);
        Assert.Contains("BuildCurrentRetailMatchCard", code, StringComparison.Ordinal);
        Assert.Contains("BuildCurrentWikidataMatchCard", code, StringComparison.Ordinal);
        Assert.Contains("IdentityLinkDisplay", code, StringComparison.Ordinal);
        Assert.Contains("BuildProviderItemUrl", code, StringComparison.Ordinal);
        Assert.Contains("https://www.themoviedb.org/", code, StringComparison.Ordinal);
        Assert.Contains("https://www.wikidata.org/wiki/", code, StringComparison.Ordinal);
        Assert.Contains("BuildCandidateChips", code, StringComparison.Ordinal);
        Assert.Contains("FormatCandidateScore", code, StringComparison.Ordinal);
        Assert.Contains("CanonicalEndpointEntityId => CurrentEntityId", code, StringComparison.Ordinal);
        Assert.Contains("CanonicalEndpointEntityId,", code, StringComparison.Ordinal);
        Assert.Contains("\"Unknown\" => \"Books\"", code, StringComparison.Ordinal);
        Assert.Contains("MediaType = _selectedMediaType", code, StringComparison.Ordinal);
        Assert.Contains("OnMatchSearchModeChanged", code, StringComparison.Ordinal);
        Assert.Contains(".sme-match-workflow", styles, StringComparison.Ordinal);
        Assert.Contains(".sme-match-result-card--selected", styles, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedEditor_MatchIdentityRefreshStaysInsideSharedEditor()
    {
        var shell = ReadSource("src/MediaEngine.Web/Components/MediaEditor/SharedMediaEditorShell.razor");
        var code = ReadSource("src/MediaEngine.Web/Components/MediaEditor/SharedMediaEditorShell.razor.cs");

        Assert.DoesNotContain("workbench", shell, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("workbench", code, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SharedMediaEditorMode.Batch", code, StringComparison.Ordinal);
        Assert.Contains("SharedMediaEditorMode.Review", code, StringComparison.Ordinal);
    }

    [Fact]
    public void EditorIdentitySummary_UsesRetailProviderNameFromContext()
    {
        var endpoint = ReadSource("src/MediaEngine.Api/Endpoints/MetadataEndpoints.cs");
        var model = ReadSource("src/MediaEngine.Domain/Models/LibraryItemModels.cs");

        Assert.Contains("detail?.RetailProviderName", endpoint, StringComparison.Ordinal);
        Assert.Contains("detail?.RetailProviderItemId", endpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("GetProviderBridgeId(detail)", endpoint, StringComparison.Ordinal);
        Assert.Contains("retail_provider_name", model, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedFieldRow_UsesCompactDetailGridAndLargerReadableFields()
    {
        var styles = ReadSource("src/MediaEngine.Web/Components/Shared/AppFormFieldRow.razor.css");

        Assert.Contains("grid-template-columns: minmax(96px, 126px) minmax(0, 1fr) 44px", styles, StringComparison.Ordinal);
        Assert.Contains("font-size: 1rem !important", styles, StringComparison.Ordinal);
        Assert.Contains("tl-field-grid--locked", styles, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedEditor_DescriptionFieldGetsMoreRoomAndPreservesParagraphSpacing()
    {
        var shell = ReadSource("src/MediaEngine.Web/Components/MediaEditor/SharedMediaEditorShell.razor");
        var code = ReadSource("src/MediaEngine.Web/Components/MediaEditor/SharedMediaEditorShell.razor.cs");
        var styles = ReadSource("src/MediaEngine.Web/Components/Shared/AppFormFieldRow.razor.css");

        Assert.Contains("Lines=\"@GetFieldLineCount(field)\"", shell, StringComparison.Ordinal);
        Assert.Contains("field.Key, \"description\"", code, StringComparison.Ordinal);
        Assert.Contains("? 7 : 4", code, StringComparison.Ordinal);
        Assert.Contains("TrimEnd()", code, StringComparison.Ordinal);
        Assert.Contains("min-height: 170px !important", styles, StringComparison.Ordinal);
        Assert.Contains("white-space: pre-wrap !important", styles, StringComparison.Ordinal);
        Assert.Contains("resize: vertical !important", styles, StringComparison.Ordinal);
        Assert.Contains("NormalizeDescriptionParagraphs", code, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedEditor_SourceFactsUseOneReadOnlySurfaceWithoutRepeatedMatchLegend()
    {
        var panelStyles = ReadSource("src/MediaEngine.Web/Components/MediaEditor/MediaEditorPanel.razor.css");
        var shellStyles = ReadSource("src/MediaEngine.Web/Components/MediaEditor/SharedMediaEditorShell.razor.css");
        var shell = ReadSource("src/MediaEngine.Web/Components/MediaEditor/SharedMediaEditorShell.razor");

        Assert.Contains(".sme-panel", panelStyles, StringComparison.Ordinal);
        Assert.Contains("sme-source-facts", shellStyles, StringComparison.Ordinal);
        Assert.Contains("Title=\"Source facts\"", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("sme-match-override-summary", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("fields overridden", shell, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedEditor_UsesInlineOverrideIndicatorsAndBlankUniverseDash()
    {
        var shell = ReadSource("src/MediaEngine.Web/Components/MediaEditor/SharedMediaEditorShell.razor");
        var code = ReadSource("src/MediaEngine.Web/Components/MediaEditor/SharedMediaEditorShell.razor.cs");
        var row = ReadSource("src/MediaEngine.Web/Components/Shared/AppFormFieldRow.razor");
        var styles = ReadSource("src/MediaEngine.Web/Components/Shared/AppFormFieldRow.razor.css");

        Assert.Contains("OverrideActive=\"@HasActiveDisplayOverride(field.Key)\"", shell, StringComparison.Ordinal);
        Assert.Contains("Unlocked=\"@IsInlineOverrideEnabled(field.Key)\"", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("Yellow underline means local override", shell, StringComparison.Ordinal);
        Assert.Contains("Source-managed facts refresh when matching changes", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("Override in use", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("Override in use", code, StringComparison.Ordinal);
        Assert.Contains("Icons.Material.Outlined.LockOpen", code, StringComparison.Ordinal);
        Assert.Contains("IsInlineOverrideEnabled(key) ? Color.Warning : Color.Default", code, StringComparison.Ordinal);
        Assert.Contains("(\"Universe\", string.IsNullOrWhiteSpace(summary?.UniverseName) ? \"-\"", code, StringComparison.Ordinal);
        Assert.Contains("tl-field-grid--override", styles, StringComparison.Ordinal);
        Assert.Contains("box-shadow: inset 0 -2px 0 var(--tl-status-warning) !important", styles, StringComparison.Ordinal);
        Assert.Contains("tl-field-grid--unlocked", row, StringComparison.Ordinal);
        Assert.Contains(".tl-field-grid--unlocked .tl-field-action-button .mud-icon-root", styles, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedEditor_RevertQueuesEmptyDisplayOverrideValue()
    {
        var code = ReadSource("src/MediaEngine.Web/Components/MediaEditor/SharedMediaEditorShell.razor.cs");

        Assert.Contains("_editedValues[scopedKey] = string.Empty;", code, StringComparison.Ordinal);
        Assert.Contains("_clearedInlineOverrideKeys.Add(scopedKey);", code, StringComparison.Ordinal);
        Assert.Contains("_pendingInlineRevertKeys.Add(scopedKey);", code, StringComparison.Ordinal);
        Assert.Contains("ConfirmInlineFieldRevertAsync", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ConfirmRemoveOverrideAsync", code, StringComparison.Ordinal);
        Assert.DoesNotContain("DialogService.ShowAsync<AppConfirmDialog>", code, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedEditor_ContainerCompletionRowsUseCompactClickableOwnedItems()
    {
        var shell = ReadSource("src/MediaEngine.Web/Components/MediaEditor/SharedMediaEditorShell.razor");
        var code = ReadSource("src/MediaEngine.Web/Components/MediaEditor/SharedMediaEditorShell.razor.cs");
        var dto = ReadSource("src/MediaEngine.Web/Models/ViewDTOs/MediaEditorContextDtos.cs");
        var libraryDto = ReadSource("src/MediaEngine.Web/Models/ViewDTOs/LibraryCatalogDtos.cs");
        var schema = ReadSource("src/MediaEngine.Web/Services/Editing/MediaEditorModels.cs");

        Assert.Contains("item.TechnicalBadges", shell, StringComparison.Ordinal);
        Assert.Contains("Disabled=\"@(!item.IsClickable)\"", shell, StringComparison.Ordinal);
        Assert.Contains("SelectContentItemAsync(group, item)", shell, StringComparison.Ordinal);
        Assert.Contains("ReturnToContainerEditorAsync", shell, StringComparison.Ordinal);
        Assert.Contains("CompactOrdinalLabel", dto, StringComparison.Ordinal);
        Assert.Contains("PrimaryAssetId", dto, StringComparison.Ordinal);
        Assert.Contains("IsClickable", dto, StringComparison.Ordinal);
        Assert.Contains("GetContentOrdinalLabel", code, StringComparison.Ordinal);
        Assert.Contains("E{ordinal:00}", code, StringComparison.Ordinal);
        Assert.Contains("T{ordinal:00}", code, StringComparison.Ordinal);
        Assert.Contains("BuildTrackContentGroups", code, StringComparison.Ordinal);
        Assert.Contains("Disc {disc}", code, StringComparison.Ordinal);
        Assert.Contains("JsonPropertyName(\"episode_title\")", libraryDto, StringComparison.Ordinal);
        Assert.Contains("detail.SeasonNumber ?? FindCanonicalValue(canonicals, \"season_number\")", schema, StringComparison.Ordinal);
        Assert.Contains("detail.EpisodeNumber ?? FindCanonicalValue(canonicals, \"episode_number\")", schema, StringComparison.Ordinal);
        Assert.Contains("detail.EpisodeTitle ?? FindCanonicalValue(canonicals, \"episode_title\")", schema, StringComparison.Ordinal);
        Assert.DoesNotContain("Field(\"episode_title\", \"Title\", identity: true)", schema, StringComparison.Ordinal);
        Assert.Contains("(\"TV\", \"episode\") => [\"episode_title\", \"description\", \"season_number\", \"episode_number\"", code, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedEditor_ConsolidatesSingleItemOptionsIntoDetailsAndKeepsHistoryOutOfFile()
    {
        var shell = ReadSource("src/MediaEngine.Web/Components/MediaEditor/SharedMediaEditorShell.razor");
        var code = ReadSource("src/MediaEngine.Web/Components/MediaEditor/SharedMediaEditorShell.razor.cs");
        var metadata = ReadSource("src/MediaEngine.Api/Endpoints/MetadataEndpoints.cs");
        var schema = ReadSource("src/MediaEngine.Web/Services/Editing/MediaEditorModels.cs");

        Assert.Contains("\"details\" => new[] { \"details\", \"options\", \"sorting\" }", code, StringComparison.Ordinal);
        Assert.DoesNotContain("tabs.Add(\"options\");", metadata, StringComparison.Ordinal);
        Assert.Contains("Source-managed facts refresh when matching changes", shell, StringComparison.Ordinal);
        Assert.Contains("GetLibraryFields()", shell, StringComparison.Ordinal);
        Assert.Contains("return [(\"details\", \"Details\", GetTabIcon(\"details\")), (\"options\", \"Options\"", code, StringComparison.Ordinal);

        var fileStart = shell.IndexOf("else if (_activeTab == \"file\")", StringComparison.Ordinal);
        var historyStart = shell.IndexOf("else if (_activeTab == \"history\")", fileStart, StringComparison.Ordinal);
        Assert.True(fileStart >= 0 && historyStart > fileStart);
        var filePanel = shell[fileStart..historyStart];
        Assert.DoesNotContain("Recent History", filePanel, StringComparison.Ordinal);
        Assert.DoesNotContain("Canonical Snapshot", filePanel, StringComparison.Ordinal);
        Assert.Contains("Change History", shell[historyStart..], StringComparison.Ordinal);

        Assert.DoesNotContain("Field(\"edition\", \"Edition\")", schema, StringComparison.Ordinal);
        Assert.Contains("Field(\"custom_tags\", \"Local tags\")", schema, StringComparison.Ordinal);
        Assert.Contains("Add(values, \"custom_tags\"", schema, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedEditor_BatchModeRequiresRealSelection()
    {
        var launcher = ReadSource("src/MediaEngine.Web/Services/Editing/MediaEditorLauncherService.cs");
        var browse = ReadSource("src/MediaEngine.Web/Components/Browse/MediaBrowseShell.razor");

        Assert.Contains("request.Mode == SharedMediaEditorMode.Batch && request.EntityIds.Count <= 1", launcher, StringComparison.Ordinal);
        Assert.Contains("_selectedItems.Count > 1", browse, StringComparison.Ordinal);
        Assert.Contains("OpenBatchEditorAsync", browse, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedEditor_DoesNotLabelBareIsbnAsOpenLibraryMatch()
    {
        var code = ReadSource("src/MediaEngine.Web/Components/MediaEditor/SharedMediaEditorShell.razor.cs");
        var start = code.IndexOf("private string? InferProviderNameFromIdentifierFields()", StringComparison.Ordinal);
        Assert.True(start >= 0);

        var end = code.IndexOf("private string FormatRetailIdentifierChip", start, StringComparison.Ordinal);
        Assert.True(end > start);

        var method = code[start..end];
        Assert.DoesNotContain("GetBaselineValue(\"isbn\")", method, StringComparison.Ordinal);
        Assert.DoesNotContain("return \"open_library\"", method, StringComparison.Ordinal);
        Assert.Contains("ISBN: {providerItemId}", code, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedEditor_ExposesHierarchyIdentityTargetsAndParentArtworkNavigation()
    {
        var shell = ReadSource("src/MediaEngine.Web/Components/MediaEditor/SharedMediaEditorShell.razor");
        var code = ReadSource("src/MediaEngine.Web/Components/MediaEditor/SharedMediaEditorShell.razor.cs");
        var context = ReadSource("src/MediaEngine.Web/Models/ViewDTOs/MediaEditorContextDtos.cs");

        Assert.Contains("aria-label=\"Edit level\"", shell, StringComparison.Ordinal);
        Assert.Contains("SelectScopeAsync(scope.ScopeId)", shell, StringComparison.Ordinal);
        Assert.Contains("Change identity for", shell, StringComparison.Ordinal);
        Assert.Contains("GetIdentityTargetImpactText", shell, StringComparison.Ordinal);
        Assert.Contains("Open @scope.Label Artwork", shell, StringComparison.Ordinal);
        Assert.Contains("private Guid CanonicalEndpointEntityId => CurrentEntityId", code, StringComparison.Ordinal);
        Assert.Contains("CoverUrl = candidate.CoverUrl", code, StringComparison.Ordinal);
        Assert.Contains("await Request.OnArtworkChanged.Invoke()", code, StringComparison.Ordinal);
        Assert.Contains("ActiveScope?.IdentitySummary", code, StringComparison.Ordinal);
        Assert.Contains("IdentityProviderItemId", ReadSource("src/MediaEngine.Domain/MetadataFieldConstants.cs"), StringComparison.Ordinal);
        Assert.Contains("JsonPropertyName(\"identity_summary\")", context, StringComparison.Ordinal);
    }

    private static string ReadSource(
        string relativePath,
        [System.Runtime.CompilerServices.CallerFilePath] string sourceFile = "") =>
        File.ReadAllText(Path.Combine(FindRepoRoot(sourceFile), relativePath));

    private static string FindRepoRoot(string sourceFile)
    {
        var directory = !string.IsNullOrWhiteSpace(sourceFile)
            ? new DirectoryInfo(Path.GetDirectoryName(sourceFile)!)
            : new DirectoryInfo(Directory.GetCurrentDirectory());

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MediaEngine.slnx")))
            directory = directory.Parent;

        if (directory is not null)
            return directory.FullName;

        directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MediaEngine.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
