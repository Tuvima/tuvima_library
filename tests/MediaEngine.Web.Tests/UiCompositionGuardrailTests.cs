using System.Text.RegularExpressions;

namespace MediaEngine.Web.Tests;

public sealed class UiCompositionGuardrailTests
{
    private static readonly string RepoRoot =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static readonly string[] MigratedFiles =
    [
        "src/MediaEngine.Web/Shared/MainLayout.razor",
        "src/MediaEngine.Web/Components/Navigation/CommandPalette.razor",
        "src/MediaEngine.Web/Components/Pages/SearchPage.razor",
        "src/MediaEngine.Web/Components/Pages/LibraryBrowsePage.razor",
        "src/MediaEngine.Web/Components/Pages/ChronicleExplorer.razor",
        "src/MediaEngine.Web/Components/Pages/Settings.razor",
        "src/MediaEngine.Web/Components/Settings/OverviewTab.razor",
        "src/MediaEngine.Web/Components/Settings/SystemTab.razor",
        "src/MediaEngine.Web/Components/Browse/MediaBrowseShell.razor",
        "src/MediaEngine.Web/Components/Collections/CollectionsPage.razor",
        "src/MediaEngine.Web/Components/Collections/CollectionEditorShell.razor",
        "src/MediaEngine.Web/Components/Library/DrawerSection.razor",
        "src/MediaEngine.Web/Components/Library/LibraryActionCenter.razor",
        "src/MediaEngine.Web/Components/Library/LibraryActionCenterCategories.razor",
        "src/MediaEngine.Web/Components/Library/LibraryAddMediaDrawer.razor",
        "src/MediaEngine.Web/Components/Library/LibraryBatchBar.razor",
        "src/MediaEngine.Web/Components/Library/LibraryCollectionDrawer.razor",
        "src/MediaEngine.Web/Components/Library/LibraryColumnPicker.razor",
        "src/MediaEngine.Web/Components/Library/LibraryConfigurableTable.razor",
        "src/MediaEngine.Web/Components/Library/LibraryDeleteConfirm.razor",
        "src/MediaEngine.Web/Components/Library/LibraryDetailDrawer.razor",
        "src/MediaEngine.Web/Components/Library/LibraryEditorFields.razor",
        "src/MediaEngine.Web/Components/Library/LibraryEditorPanel.razor",
        "src/MediaEngine.Web/Components/Library/LibraryPage.razor",
        "src/MediaEngine.Web/Components/Library/LibraryPeopleTable.razor",
        "src/MediaEngine.Web/Components/Library/LibraryUniverseUnlinked.razor",
        "src/MediaEngine.Web/Components/Library/LibraryUniversesTable.razor",
        "src/MediaEngine.Web/Components/Library/LibraryToolbar.razor",
        "src/MediaEngine.Web/Components/Library/StatusPill.razor",
        "src/MediaEngine.Web/Components/MediaEditor/SharedMediaBatchConfirmDialog.razor",
        "src/MediaEngine.Web/Components/MediaEditor/SharedMediaEditorShell.razor",
    ];

    private static readonly Regex RetiredCustomUiRegex =
        new(@"<(?:AppTabs|AppPageHeader|AppSurfaceCard|AppIcon|TopBar|ProfileDropdown|LibraryToolbar)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RawInteractiveHtmlRegex =
        new(@"<(?:button|input|select|textarea|table|dialog|details|summary)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RazorCommentRegex =
        new(@"@\*.*?\*@", RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex LineCommentRegex =
        new(@"(?m)^\s*///?.*$", RegexOptions.Compiled);

    public static IEnumerable<object[]> MigratedFilesData() =>
        MigratedFiles.Select(path => new object[] { path });

    [Theory]
    [MemberData(nameof(MigratedFilesData))]
    public void MigratedFiles_DoNotUseRetiredCustomUiOrRawInteractiveHtml(string relativePath)
    {
        var filePath = Path.Combine(RepoRoot, relativePath);
        var contents = Sanitize(File.ReadAllText(filePath));

        Assert.False(
            RetiredCustomUiRegex.IsMatch(contents),
            $"Retired custom UI wrapper found in {relativePath}.");

        Assert.False(
            RawInteractiveHtmlRegex.IsMatch(contents),
            $"Raw interactive HTML found in {relativePath}.");
    }

    private static string Sanitize(string contents)
    {
        var withoutRazorComments = RazorCommentRegex.Replace(contents, string.Empty);
        return LineCommentRegex.Replace(withoutRazorComments, string.Empty);
    }
}
