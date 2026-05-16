namespace MediaEngine.Web.Tests;

public sealed class Phase5InlineEditingTests
{
    [Fact]
    public void DetailPage_EditActionLaunchesSharedEditor()
    {
        var source = ReadSource("src/MediaEngine.Web/Components/Details/DetailPage.razor");

        Assert.Contains("MediaEditorLauncherService MediaEditorLauncher", source, StringComparison.Ordinal);
        Assert.Contains("action.Key is \"edit-media\" or \"edit\"", source, StringComparison.Ordinal);
        Assert.Contains("MediaEditorLauncher.OpenAsync(new MediaEditorLaunchRequest", source, StringComparison.Ordinal);
        Assert.Contains("Mode = SharedMediaEditorMode.Normal", source, StringComparison.Ordinal);
    }

    [Fact]
    public void BrowseRowAndCard_EditActionsLaunchSharedEditor()
    {
        var browse = ReadSource("src/MediaEngine.Web/Components/Browse/MediaBrowseShell.razor");
        var table = ReadSource("src/MediaEngine.Web/Components/Library/LibraryConfigurableTable.razor");
        var card = ReadSource("src/MediaEngine.Web/Components/Discovery/DiscoveryCard.razor");
        var group = ReadSource("src/MediaEngine.Web/Components/Library/MediaGroupPage.razor");

        Assert.Contains("MediaEditorLauncherService MediaEditorLauncher", browse, StringComparison.Ordinal);
        Assert.Contains("OnEditClicked=\"OpenItemEditorAsync\"", browse, StringComparison.Ordinal);
        Assert.Contains("OnEditClicked=\"OpenCardEditorAsync\"", browse, StringComparison.Ordinal);
        Assert.Contains("OnItemEditClicked=\"OpenItemEditorAsync\"", browse, StringComparison.Ordinal);
        Assert.Contains("MediaEditorLauncher.OpenAsync(new MediaEditorLaunchRequest", browse, StringComparison.Ordinal);
        Assert.Contains("OnEditClicked.InvokeAsync(item.EntityId)", table, StringComparison.Ordinal);
        Assert.Contains("OnEditClicked.InvokeAsync(Item)", card, StringComparison.Ordinal);
        Assert.Contains("isOwned && OnItemEditClicked.HasDelegate", group, StringComparison.Ordinal);
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

        Assert.Contains("Title=\"Metadata\"", shell, StringComparison.Ordinal);
        Assert.Contains("Class=\"sme-match-info-panel\"", shell, StringComparison.Ordinal);
        Assert.Contains("Match Information", shell, StringComparison.Ordinal);
        Assert.Contains("ActionIcon=\"@GetInlineFieldActionIcon(field.Key)\"", shell, StringComparison.Ordinal);
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
        Assert.Contains("SaveItemDisplayOverridesAsync(scopeGroup.Key.EntityId, overrideFields)", code, StringComparison.Ordinal);
        Assert.Contains("SaveItemPreferencesAsync(scopeGroup.Key.EntityId, preferenceFields)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedEditor_MatchInformationUsesProviderNameAndOmitsTechnicalRows()
    {
        var code = ReadSource("src/MediaEngine.Web/Components/MediaEditor/SharedMediaEditorShell.razor.cs");

        Assert.Contains("GetRetailMatchDisplayName(summary)", code, StringComparison.Ordinal);
        Assert.Contains("(\"Retail Match\", !string.IsNullOrWhiteSpace(provider) ? provider : hasProviderEvidence ? \"Retail matched\" : \"Not linked\")", code, StringComparison.Ordinal);
        Assert.DoesNotContain("(\"Provider ID\"", code, StringComparison.Ordinal);
        Assert.DoesNotContain("(\"Match Source\"", code, StringComparison.Ordinal);
        Assert.DoesNotContain("NormalizeRetailProviderLabel(summary?.MatchSource)", code, StringComparison.Ordinal);
        Assert.Contains("Guid.TryParse(trimmed, out _)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ProviderNameFromBridgeIdentifier", code, StringComparison.Ordinal);
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
    public void SharedEditor_MatchInformationPanelUsesSeparatedHighlightedSurface()
    {
        var panelStyles = ReadSource("src/MediaEngine.Web/Components/MediaEditor/MediaEditorPanel.razor.css");
        var shellStyles = ReadSource("src/MediaEngine.Web/Components/MediaEditor/SharedMediaEditorShell.razor.css");
        var shell = ReadSource("src/MediaEngine.Web/Components/MediaEditor/SharedMediaEditorShell.razor");

        Assert.Contains(".sme-match-info-panel", panelStyles, StringComparison.Ordinal);
        Assert.Contains("radial-gradient", panelStyles, StringComparison.Ordinal);
        Assert.Contains("sme-match-info-list", shellStyles, StringComparison.Ordinal);
        Assert.Contains("border: 1px solid rgba(148, 163, 184, 0.14)", shellStyles, StringComparison.Ordinal);
        Assert.Contains("sme-match-override-summary", shell, StringComparison.Ordinal);
        Assert.Contains("fields overridden", shell, StringComparison.Ordinal);
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
        Assert.Contains("sme-metadata-legend-icon--unlock", shell, StringComparison.Ordinal);
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
        Assert.Contains("Field(\"episode_title\", \"Title\", identity: true)", schema, StringComparison.Ordinal);
        Assert.Contains("(\"TV\", \"episode\") => [\"episode_title\", \"description\", \"season_number\", \"episode_number\"", code, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedEditor_OptionsTabUsesCentralizedLocalOptionsComponent()
    {
        var shell = ReadSource("src/MediaEngine.Web/Components/MediaEditor/SharedMediaEditorShell.razor");
        var code = ReadSource("src/MediaEngine.Web/Components/MediaEditor/SharedMediaEditorShell.razor.cs");
        var component = ReadSource("src/MediaEngine.Web/Components/MediaEditor/MediaEditorLocalOptions.razor");
        var schema = ReadSource("src/MediaEngine.Web/Services/Editing/MediaEditorModels.cs");

        Assert.Contains("<MediaEditorLocalOptions", shell, StringComparison.Ordinal);
        Assert.Contains("TagValue=\"@GetEditableValue(\"custom_tags\")\"", shell, StringComparison.Ordinal);
        Assert.Contains("GetAdditionalOptionsGroups", code, StringComparison.Ordinal);
        Assert.Contains("key is \"genre\" or \"custom_tags\" or \"rating\" or \"comment\"", code, StringComparison.Ordinal);
        Assert.Contains("Genres <span>(Local)</span>", component, StringComparison.Ordinal);
        Assert.Contains("Tags <span>(Local)</span>", component, StringComparison.Ordinal);
        Assert.Contains("User Notes", component, StringComparison.Ordinal);
        Assert.Contains("These notes are private and not written to metadata.", component, StringComparison.Ordinal);
        Assert.Contains("MudChip", component, StringComparison.Ordinal);
        Assert.Contains("MudIconButton", component, StringComparison.Ordinal);
        Assert.Contains("Field(\"custom_tags\", \"Tags\")", schema, StringComparison.Ordinal);
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
    public void Source_DoesNotReintroduceVaultWorkflow()
    {
        var root = FindRepoRoot();
        var offenders = Directory.EnumerateFiles(Path.Combine(root, "src"), "*", SearchOption.AllDirectories)
            .Where(path => Path.GetExtension(path) is ".cs" or ".razor" or ".css")
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path =>
            {
                var text = File.ReadAllText(path);
                return text.Contains("/vault", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("VaultPage", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("LibrarySurfacePreset", StringComparison.OrdinalIgnoreCase);
            })
            .Select(path => Path.GetRelativePath(root, path))
            .ToArray();

        Assert.Empty(offenders);
    }

    private static string ReadSource(string relativePath) =>
        File.ReadAllText(Path.Combine(FindRepoRoot(), relativePath));

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MediaEngine.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
