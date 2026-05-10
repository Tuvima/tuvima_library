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

        Assert.Contains("Save and Resolve Review", shell, StringComparison.Ordinal);
        Assert.Contains("Approve Current Metadata", shell, StringComparison.Ordinal);
        Assert.Contains("ResolveReviewWithoutChangesAsync", code, StringComparison.Ordinal);
        Assert.Contains("Orchestrator.ResolveReviewAsync", code, StringComparison.Ordinal);
        Assert.Contains("Review was not resolved because changes could not be saved.", code, StringComparison.Ordinal);
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
