namespace MediaEngine.Web.Tests;

public sealed class Wave5AccessibilityGuardrailTests
{
    private static readonly string RepoRoot =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    public void MainLayout_HasSkipLinkAndMainLandmark()
    {
        var source = Read("src/MediaEngine.Web/Shared/MainLayout.razor");

        Assert.Contains("href=\"#main-content\"", source);
        Assert.Contains("id=\"main-content\"", source);
        Assert.Contains("role=\"main\"", source);
    }

    [Fact]
    public void LibraryDetailDrawer_HasDialogSemanticsAndAccessibleClose()
    {
        var drawer = Read("src/MediaEngine.Web/Components/Library/LibraryDetailDrawer.razor");
        var header = Read("src/MediaEngine.Web/Components/Library/DetailDrawer/LibraryDetailDrawerHeader.razor");

        Assert.Contains("role=\"dialog\"", drawer);
        Assert.Contains("aria-modal=\"true\"", drawer);
        Assert.Contains("aria-label=\"Close details drawer\"", header);
    }

    [Fact]
    public void SharedDialogShell_HasDialogLabelAndAccessibleClose()
    {
        var source = Read("src/MediaEngine.Web/Components/Shared/AppDialogShell.razor");

        Assert.Contains("role=\"dialog\"", source);
        Assert.Contains("aria-labelledby", source);
        Assert.Contains("aria-label=\"Close dialog\"", source);
    }

    [Fact]
    public void Editors_IncludeNavigationLockForUnsavedChanges()
    {
        Assert.Contains("NavigationLock", Read("src/MediaEngine.Web/Components/Settings/MediaItemEditor.razor"));
        Assert.Contains("NavigationLock", Read("src/MediaEngine.Web/Components/Collections/CollectionEditorShell.razor"));
        Assert.Contains("NavigationLock", Read("src/MediaEngine.Web/Components/MediaEditor/SharedMediaEditorShell.razor"));
    }

    private static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoRoot, relativePath));
}
