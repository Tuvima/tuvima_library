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
        "src/MediaEngine.Web/Components/Settings/SettingsReviewQueueTab.razor",
        "src/MediaEngine.Web/Components/Browse/MediaBrowseShell.razor",
        "src/MediaEngine.Web/Components/Pages/Collections.razor",
        "src/MediaEngine.Web/Components/Collections/CollectionEditorShell.razor",
        "src/MediaEngine.Web/Components/Library/LibraryBatchBar.razor",
        "src/MediaEngine.Web/Components/Library/LibraryColumnPicker.razor",
        "src/MediaEngine.Web/Components/Library/LibraryConfigurableTable.razor",
        "src/MediaEngine.Web/Components/Library/LibraryDeleteConfirm.razor",
        "src/MediaEngine.Web/Components/Library/StatusPill.razor",
        "src/MediaEngine.Web/Components/Details/DetailPage.razor",
        "src/MediaEngine.Web/Components/MediaEditor/SharedMediaBatchConfirmDialog.razor",
        "src/MediaEngine.Web/Components/MediaEditor/SharedMediaEditorShell.razor",
    ];

    private static readonly string[] RetiredVaultFiles =
    [
        "src/MediaEngine.Web/Components/Library/LibraryPage.razor",
        "src/MediaEngine.Web/Models/ViewDTOs/LibrarySurfacePreset.cs",
        "src/MediaEngine.Web/Components/Library/LibraryActionCenter.razor",
        "src/MediaEngine.Web/Components/Library/LibraryActionCenterCategories.razor",
        "src/MediaEngine.Web/Components/Library/LibraryAddMediaDrawer.razor",
        "src/MediaEngine.Web/Components/Library/LibraryCollectionDrawer.razor",
        "src/MediaEngine.Web/Components/Library/LibraryOverviewPanel.razor",
        "src/MediaEngine.Web/Components/Library/LibraryPeopleTable.razor",
        "src/MediaEngine.Web/Components/Library/LibraryToolbar.razor",
        "src/MediaEngine.Web/Components/Library/LibraryUniversesTable.razor",
        "src/MediaEngine.Web/Components/Library/LibraryUniverseAssigned.razor",
        "src/MediaEngine.Web/Components/Library/LibraryUniversePendingQueue.razor",
        "src/MediaEngine.Web/Components/Library/LibraryUniverseUnlinked.razor",
        "src/MediaEngine.Web/Components/Library/LibraryEditorPanel.razor",
        "src/MediaEngine.Web/Components/Library/LibraryEditorFields.razor",
    ];

    private static readonly Regex RetiredCustomUiRegex =
        new(@"<(?:AppTabs|AppPageHeader|AppSurfaceCard|AppIcon|TopBar|ProfileDropdown|LibraryToolbar)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RawInteractiveHtmlRegex =
        new(@"<(?:button|input|select|textarea|table|dialog|details|summary)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ActiveVaultWorkflowRegex =
        new(@"\bLibraryPage\b|\bLibrarySurfacePreset\b|vault-|Library Vault|>\s*Vault\s*<",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ActiveVaultDocsRegex =
        new(@"\bLibrary Vault\b|\bVault\b.*\b(current|feature|workflow|page|tab|surface|workspace)\b|\b/vault\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RazorCommentRegex =
        new(@"@\*.*?\*@", RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex LineCommentRegex =
        new(@"(?m)^\s*///?.*$", RegexOptions.Compiled);

    public static IEnumerable<object[]> MigratedFilesData() =>
        MigratedFiles.Select(path => new object[] { path });

    public static IEnumerable<object[]> RetiredVaultFilesData() =>
        RetiredVaultFiles.Select(path => new object[] { path });

    [Theory]
    [MemberData(nameof(MigratedFilesData))]
    public void MigratedFiles_DoNotUseRetiredCustomUiOrRawInteractiveHtml(string relativePath)
    {
        var filePath = Path.Combine(RepoRoot, relativePath);
        Assert.True(File.Exists(filePath), $"Guardrail target is missing: {relativePath}.");
        var contents = Sanitize(File.ReadAllText(filePath));

        Assert.False(
            RetiredCustomUiRegex.IsMatch(contents),
            $"Retired custom UI wrapper found in {relativePath}.");

        Assert.False(
            RawInteractiveHtmlRegex.IsMatch(contents),
            $"Raw interactive HTML found in {relativePath}.");
    }

    [Theory]
    [MemberData(nameof(RetiredVaultFilesData))]
    public void RetiredVaultFiles_DoNotExist(string relativePath)
    {
        Assert.False(
            File.Exists(Path.Combine(RepoRoot, relativePath)),
            $"{relativePath} belongs to the retired Vault workflow and should not be restored.");
    }

    [Fact]
    public void ActiveDashboardFiles_DoNotReintroduceVaultWorkflow()
    {
        var roots = new[]
        {
            Path.Combine(RepoRoot, "src", "MediaEngine.Web", "Components"),
            Path.Combine(RepoRoot, "src", "MediaEngine.Web", "Shared"),
            Path.Combine(RepoRoot, "src", "MediaEngine.Web", "Models", "ViewDTOs"),
        };

        var offenders = roots
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
            .Where(path => path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
            .Where(path => !RetiredVaultFiles.Contains(ToRelativePath(path), StringComparer.OrdinalIgnoreCase))
            .Where(path => ActiveVaultWorkflowRegex.IsMatch(Sanitize(File.ReadAllText(path))))
            .Select(ToRelativePath)
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void ActiveDocs_DoNotDescribeVaultAsCurrentFeature()
    {
        var roots = new[]
        {
            Path.Combine(RepoRoot, "README.md"),
            Path.Combine(RepoRoot, "docs"),
            Path.Combine(RepoRoot, ".agent"),
        };

        var allowedHistoricalFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "README.md",
            "docs/architecture/dashboard-ui.md",
            "docs/architecture/target-state.md",
            "docs/design-system/README.md",
            "docs/design-system/SKILL.md",
            "docs/guides/running-tests.md",
            ".agent/FIX-PLAN.md",
            ".agent/features/LIBRARY-DASHBOARD.md",
            ".agent/skills/DASHBOARD-UI.md",
        };

        var offenders = roots
            .SelectMany(root =>
            {
                if (File.Exists(root))
                    return [root];
                return Directory.Exists(root)
                    ? Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories)
                    : [];
            })
            .Where(path => !allowedHistoricalFiles.Contains(ToRelativePath(path)))
            .Where(path => ActiveVaultDocsRegex.IsMatch(Sanitize(File.ReadAllText(path))))
            .Select(ToRelativePath)
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void ActiveNavigationResources_DoNotExposeVaultLabel()
    {
        var resourceRoot = Path.Combine(RepoRoot, "src", "MediaEngine.Web", "Resources");
        var offenders = Directory.EnumerateFiles(resourceRoot, "*.resx", SearchOption.AllDirectories)
            .Where(path => Regex.IsMatch(File.ReadAllText(path), @"<value>\s*Vault\s*</value>", RegexOptions.IgnoreCase))
            .Select(ToRelativePath)
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void SharedMediaEditorLauncher_RemainsCanonicalEditingPath()
    {
        var detailPage = File.ReadAllText(Path.Combine(RepoRoot, "src", "MediaEngine.Web", "Components", "Details", "DetailPage.razor"));
        var reviewTab = File.ReadAllText(Path.Combine(RepoRoot, "src", "MediaEngine.Web", "Components", "Settings", "SettingsReviewQueueTab.razor"));
        var bookDetail = File.ReadAllText(Path.Combine(RepoRoot, "src", "MediaEngine.Web", "Components", "Universe", "BookDetailContent.razor"));
        var launcher = File.ReadAllText(Path.Combine(RepoRoot, "src", "MediaEngine.Web", "Services", "Editing", "MediaEditorLauncherService.cs"));

        Assert.Contains("MediaEditorLauncher.OpenAsync", detailPage);
        Assert.Contains("MediaEditorLauncher.OpenAsync", reviewTab);
        Assert.Contains("MediaEditorLauncher.OpenAsync", bookDetail);
        Assert.Contains("SharedMediaEditorMode.Normal", detailPage);
        Assert.Contains("SharedMediaEditorMode.Review", reviewTab);
        Assert.Contains("SharedMediaEditorMode.Batch", launcher);
    }

    [Fact]
    public void InlineEditingSurfaces_RefreshOnlyAfterSuccessfulEditorResult()
    {
        var detailPage = File.ReadAllText(Path.Combine(RepoRoot, "src", "MediaEngine.Web", "Components", "Details", "DetailPage.razor"));
        var reviewTab = File.ReadAllText(Path.Combine(RepoRoot, "src", "MediaEngine.Web", "Components", "Settings", "SettingsReviewQueueTab.razor"));
        var listenPage = File.ReadAllText(Path.Combine(RepoRoot, "src", "MediaEngine.Web", "Components", "Pages", "ListenPage.razor.cs"));

        Assert.Matches(@"if\s*\(\s*applied\s*\)\s*\{[^}]*GetDetailPageAsync", detailPage);
        Assert.Matches(@"if\s*\(\s*applied\s*\)\s*\{[^}]*LoadAsync", reviewTab);
        Assert.Matches(@"if\s*\(\s*applied\s*\)\s*\{[^}]*LoadAsync", listenPage);
    }

    [Fact]
    public void MediaEditorLauncher_ProtectsEmptyAndMultiItemBatchLaunches()
    {
        var launcher = File.ReadAllText(Path.Combine(RepoRoot, "src", "MediaEngine.Web", "Services", "Editing", "MediaEditorLauncherService.cs"));

        Assert.Contains("request.EntityIds.Count == 0", launcher);
        Assert.Contains("return false", launcher);
        Assert.Contains("request.Mode == SharedMediaEditorMode.Batch && request.EntityIds.Count > 1", launcher);
        Assert.Contains("SharedMediaBatchConfirmDialog", launcher);
        Assert.Contains("confirmResult is null || confirmResult.Canceled", launcher);
    }

    private static string Sanitize(string contents)
    {
        var withoutRazorComments = RazorCommentRegex.Replace(contents, string.Empty);
        return LineCommentRegex.Replace(withoutRazorComments, string.Empty);
    }

    private static string ToRelativePath(string path) =>
        Path.GetRelativePath(RepoRoot, path).Replace('\\', '/');
}
