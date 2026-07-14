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
    public void SharedDialogShell_HasDialogLabelAndAccessibleClose()
    {
        var source = Read("src/MediaEngine.Web/Components/Shared/AppDialogShell.razor");

        Assert.Contains("role=\"dialog\"", source);
        Assert.Contains("aria-labelledby", source);
        Assert.Contains("aria-label=\"Close dialog\"", source);
    }

    [Fact]
    public void ActiveEditors_IncludeNavigationLockForUnsavedChanges()
    {
        Assert.Contains("NavigationLock", Read("src/MediaEngine.Web/Components/Collections/CollectionEditorShell.razor"));
        Assert.Contains("NavigationLock", Read("src/MediaEngine.Web/Components/MediaEditor/SharedMediaEditorShell.razor"));
    }

    [Fact]
    public void ReaderAndBookCover_UseNativeControlsForClickActions()
    {
        var reader = Read("src/MediaEngine.Web/Components/Pages/EpubReader.razor");
        var book = Read("src/MediaEngine.Web/Components/Universe/BookDetailContent.razor");
        var folderBrowser = Read("src/MediaEngine.Web/Components/Settings/FolderBrowserDialog.razor");

        Assert.Contains("Class=\"reader-tap-zone left\" AriaLabel=\"Previous page\"", reader);
        Assert.Contains("Class=\"reader-search-result\"", reader);
        Assert.Contains("Class=\"reader-list-open\"", reader);
        Assert.DoesNotContain("<div class=\"reader-search-result\"", reader);
        Assert.Contains("Class=\"book-detail-cover-wrap\"", book);
        Assert.Contains("role=\"dialog\"", book);
        Assert.Contains("AriaLabel=\"Close cover\"", book);
        Assert.Contains("AriaLabel=\"@($\"Open author {_authorPerson.Name}\")\"", book);
        Assert.DoesNotContain("Nav.NavigateTo($\" /details/person/", book);
        Assert.Contains("Class=\"folder-browser-row\"", folderBrowser);
        Assert.Contains("AriaLabel=\"@($\"Open folder {dir}\")\"", folderBrowser);
        Assert.DoesNotContain("<div class=\"folder-browser-row\"", folderBrowser);
    }

    [Fact]
    public void ActiveSelectableSurfaces_ProvideKeyboardActivation()
    {
        var formats = Read("src/MediaEngine.Web/Components/Details/OwnedFormatsPanel.razor");
        var plugins = Read("src/MediaEngine.Web/Components/Settings/PluginSettingsTab.razor");
        var providers = Read("src/MediaEngine.Web/Components/Settings/ProviderPriorityTab.razor");
        var activity = Read("src/MediaEngine.Web/Components/Activity/ActivityMediaTypeAuditGroup.razor");

        Assert.Contains("@onkeydown=\"@(args => HandleFormatKeyDown(args, format.Id))\"", formats);
        Assert.DoesNotContain("tl-detail-mini-action", formats);
        Assert.Contains("HandlePluginKeyDown", plugins);
        Assert.Contains("HandleActivationKey", providers);
        Assert.Contains("<AppNativeButton Type=\"button\"", providers);
        Assert.Contains("HandleItemKeyDownAsync", activity);
    }

    [Fact]
    public void ArtworkPreview_RemainsAnAccessibleFocusedComponent()
    {
        var source = Read("src/MediaEngine.Web/Components/MediaEditor/MediaEditorArtworkLightbox.razor");

        Assert.Contains("role=\"dialog\"", source);
        Assert.Contains("aria-modal=\"true\"", source);
        Assert.Contains("aria-label=\"Close artwork preview\"", source);
    }

    private static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoRoot, relativePath));
}
