using System.Text.RegularExpressions;

namespace MediaEngine.Api.Tests;

public sealed class DatabaseConnectionGuardrailTests
{
    [Fact]
    public void EndpointFiles_DoNotUseSharedDatabaseOpen()
    {
        var repoRoot = FindRepoRoot();
        var endpointDir = Path.Combine(repoRoot, "src", "MediaEngine.Api", "Endpoints");
        var offenders = Directory.EnumerateFiles(endpointDir, "*.cs", SearchOption.AllDirectories)
            .Select(path => new
            {
                Path = path,
                Text = File.ReadAllText(path),
            })
            .Where(file => file.Text.Contains("db.Open()", StringComparison.Ordinal)
                           || file.Text.Contains(".Open();", StringComparison.Ordinal)
                              && file.Text.Contains("IDatabaseConnection", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(repoRoot, file.Path))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void ActiveSourceFiles_DoNotUseSharedDatabaseOpenOutsideStartupSchemaOrFixtures()
    {
        var repoRoot = FindRepoRoot();
        var offenders = Directory.EnumerateFiles(Path.Combine(repoRoot, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !AllowedOpenUsageFiles.Contains(ToRelativePath(repoRoot, path), StringComparer.OrdinalIgnoreCase))
            .Where(path =>
            {
                var text = File.ReadAllText(path);
                return text.Contains("IDatabaseConnection", StringComparison.Ordinal)
                    && (text.Contains(".Open()", StringComparison.Ordinal)
                        || text.Contains(".Open();", StringComparison.Ordinal)
                        || text.Contains("db.Open(", StringComparison.Ordinal)
                        || text.Contains("_db.Open(", StringComparison.Ordinal));
            })
            .Select(path => ToRelativePath(repoRoot, path))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void SilentCatchBlocks_AreLimitedToDocumentedBestEffortLocations()
    {
        var repoRoot = FindRepoRoot();
        var offenders = Directory.EnumerateFiles(Path.Combine(repoRoot, "src"), "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(repoRoot, "src"), "*.razor", SearchOption.AllDirectories))
            .Where(path =>
            {
                var relative = ToRelativePath(repoRoot, path);
                return !AllowedSilentCatchFiles.Contains(relative, StringComparer.OrdinalIgnoreCase)
                    && SilentCatchRegex.IsMatch(File.ReadAllText(path));
            })
            .Select(path => ToRelativePath(repoRoot, path))
            .ToList();

        Assert.Empty(offenders);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MediaEngine.slnx")))
            dir = dir.Parent;

        return dir?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }

    private static string ToRelativePath(string repoRoot, string path) =>
        Path.GetRelativePath(repoRoot, path).Replace('\\', '/');

    private static readonly string[] AllowedOpenUsageFiles =
    [
        // Startup/schema lifecycle owns the shared connection.
        "src/MediaEngine.Api/Program.cs",
        "src/MediaEngine.Storage/DatabaseConnection.cs",
        // Dev harness reset is a non-production database maintenance path.
        "src/MediaEngine.Api/DevSupport/DevHarnessResetService.cs",
    ];

    private static readonly string[] AllowedSilentCatchFiles =
    [
        // TODO Phase 5: replace these legacy best-effort catches with logged degraded states.
        "src/MediaEngine.Api/Program.cs",
        "src/MediaEngine.Api/Endpoints/ItemCanonicalEndpoints.cs",
        "src/MediaEngine.Api/Services/Plugins/PluginSegmentDetectionService.cs",
        "src/MediaEngine.Api/Services/Plugins/PluginToolRuntime.cs",
        "src/MediaEngine.AI/Features/AudioSimilarityService.cs",
        "src/MediaEngine.AI/Infrastructure/ResourceMonitorService.cs",
        "src/MediaEngine.AI/Llama/LlamaInferenceService.cs",
        "src/MediaEngine.Ingestion/AutoOrganizeService.cs",
        "src/MediaEngine.Ingestion/IngestionEngine.cs",
        "src/MediaEngine.Storage/ConfigurationDirectoryLoader.cs",
        "src/MediaEngine.Storage/DatabaseConnection.cs",
        "src/MediaEngine.Storage/Models/CoreConfiguration.cs",
        "src/MediaEngine.Web/Components/Collections/CollectionEditorShell.razor",
        "src/MediaEngine.Web/Components/Discovery/DiscoveryCard.razor",
        "src/MediaEngine.Web/Components/Library/LibraryColumnPicker.razor",
        "src/MediaEngine.Web/Components/LibraryItems/InspectorCoverPicker.razor",
        "src/MediaEngine.Web/Components/LibraryItems/InspectorSearchSection.razor",
        "src/MediaEngine.Web/Components/Listen/ListenNowPlayingBar.razor",
        "src/MediaEngine.Web/Components/Listen/ListenTrackDataGrid.razor",
        "src/MediaEngine.Web/Components/Pages/ChronicleExplorer.razor",
        "src/MediaEngine.Web/Components/Pages/EpubReader.razor",
        "src/MediaEngine.Web/Components/Pages/ListenPage.razor.cs",
        "src/MediaEngine.Web/Components/Pages/ListenPlayerPopupPage.razor",
        "src/MediaEngine.Web/Components/Pages/WatchPlayerPage.razor",
        "src/MediaEngine.Web/Components/Settings/MediaItemEditor.razor",
        "src/MediaEngine.Web/Components/Settings/ProviderPriorityTab.razor",
        "src/MediaEngine.Web/Components/Settings/UniverseSettingsTab.razor",
        "src/MediaEngine.Web/Components/Universe/BookDetailContent.razor",
        "src/MediaEngine.Web/Components/Universe/SwimlaneSection.razor",
    ];

    private static readonly Regex SilentCatchRegex =
        new(@"catch\s*\{\s*\}", RegexOptions.Compiled);
}
