namespace MediaEngine.Web.Tests;

public sealed class UiArchitectureGuardrailTests
{
    private static readonly string RepoRoot =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    public void RetiredLibraryItemsWorkspace_RemainsRemoved()
    {
        var directory = Path.Combine(RepoRoot, "src", "MediaEngine.Web", "Components", "LibraryItems");
        var activeComponents = Directory.GetFiles(directory, "*.razor")
            .Select(path => Path.GetFileNameWithoutExtension(path)!)
            .OrderBy(name => name)
            .ToArray();

        Assert.Equal(["ReportProblemDialog"], activeComponents);
    }

    [Theory]
    [InlineData("src/MediaEngine.Web/Components/Library/SearchResultCard.razor")]
    [InlineData("src/MediaEngine.Web/Components/Library/LibraryMusicGrid.razor")]
    [InlineData("src/MediaEngine.Web/Components/Playback/ReaderHighlightsPanel.razor")]
    [InlineData("src/MediaEngine.Web/Components/Universe/CollectionHero.razor")]
    [InlineData("src/MediaEngine.Web/Components/Universe/MediaSearchPanel.razor")]
    [InlineData("src/MediaEngine.Web/Components/Universe/PersonCard.razor")]
    [InlineData("src/MediaEngine.Web/Components/Universe/TrackRow.razor")]
    public void RetiredStandaloneComponents_RemainRemoved(string relativePath)
    {
        Assert.False(File.Exists(Path.Combine(RepoRoot, relativePath)));
    }

    [Fact]
    public void ManagedCollectionClientFeature_IsolatedWithoutMovingAiMethods()
    {
        var facade = Read("src/MediaEngine.Web/Services/Integration/EngineApiClient.cs");
        var features = new[]
        {
            Read("src/MediaEngine.Web/Services/Integration/EngineApiClient.ManagedCollections.cs"),
            Read("src/MediaEngine.Web/Services/Integration/EngineApiClient.LibraryOperations.cs"),
            Read("src/MediaEngine.Web/Services/Integration/EngineApiClient.CollectionGroups.cs")
        };

        Assert.Contains("public sealed partial class EngineApiClient", facade);
        Assert.Contains("GetManagedCollectionsAsync", features[0]);
        Assert.Contains("GetCollectionCatalogAsync", features[0]);
        Assert.Contains("NormalizeManagedCollectionArtwork", features[0]);
        Assert.All(features, feature =>
            Assert.DoesNotContain("/ai/", feature, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EngineApiClient_RemainsSplitByCohesiveArea()
    {
        var areas = new Dictionary<string, string>
        {
            ["Details"] = "GetDetailPageAsync",
            ["Display"] = "GetDisplayHomeAsync",
            ["Operations"] = "GetIngestionOperationsSnapshotAsync",
            ["People"] = "GetPersonsAsync",
            ["Playback"] = "GetPlaybackManifestAsync",
            ["SearchMisc"] = "SearchWorksAsync",
            ["Settings"] = "GetServerGeneralAsync",
        };

        foreach (var (area, representativeMethod) in areas)
        {
            var implementation = Read(
                $"src/MediaEngine.Web/Services/Integration/EngineApiClient.{area}.cs");
            var contract = Read(
                $"src/MediaEngine.Web/Services/Integration/IEngineApiClient.{area}.cs");

            Assert.Contains("public sealed partial class EngineApiClient", implementation);
            Assert.Contains("public partial interface IEngineApiClient", contract);
            Assert.Contains(representativeMethod, implementation);
            Assert.Contains(representativeMethod, contract);
        }

        var facade = Read("src/MediaEngine.Web/Services/Integration/EngineApiClient.cs");
        var interfaceFacade = Read("src/MediaEngine.Web/Services/Integration/IEngineApiClient.cs");

        Assert.True(facade.Split('\n').Length <= 2_600);
        Assert.True(interfaceFacade.Split('\n').Length <= 230);
        Assert.Contains("private async Task<T?> GetAsync<T>(", facade);
        Assert.Contains("GetUniverseGraphAsync", facade);
        Assert.Contains("SearchUniverseAsync", facade);
        Assert.Contains("GetUniverseHealthAsync", facade);
        Assert.Contains("GetUniverseGraphAsync", interfaceFacade);
        Assert.Contains("SearchUniverseAsync", interfaceFacade);
        Assert.Contains("GetUniverseHealthAsync", interfaceFacade);
    }

    [Fact]
    public void EmptyStagedApiClients_RemainRemoved()
    {
        Assert.False(File.Exists(Path.Combine(
            RepoRoot, "src", "MediaEngine.Web", "Services", "Integration", "Clients", "LibraryClient.cs")));
        Assert.False(File.Exists(Path.Combine(
            RepoRoot, "src", "MediaEngine.Web", "Services", "Integration", "Clients", "CollectionClient.cs")));

        var program = Read("src/MediaEngine.Web/Program.cs");
        Assert.DoesNotContain("AddScoped<LibraryClient>", program);
        Assert.DoesNotContain("AddScoped<CollectionClient>", program);
    }

    [Fact]
    public void LibraryItemHelpers_ContainOnlyActiveMediaTypeFormatting()
    {
        var helpers = Read("src/MediaEngine.Web/Components/LibraryItems/LibraryItemHelpers.cs");

        Assert.Contains("GetMediaTypeIcon", helpers);
        Assert.Contains("FormatMediaType", helpers);
        Assert.DoesNotContain("GetStatusColor", helpers);
        Assert.DoesNotContain("GetStatusChipStyle", helpers);
        Assert.DoesNotContain("GetConfidenceColor", helpers);
        Assert.DoesNotContain("Legacy statuses", helpers);
    }

    [Fact]
    public void IngestionDashboard_BackgroundLoopsAreOwnedAndAwaited()
    {
        var state = Read("src/MediaEngine.Web/Services/Integration/IngestionLiveDashboardState.cs");
        var component = Read("src/MediaEngine.Web/Components/Settings/IngestionTasksTab.razor");

        Assert.Contains("IDisposable, IAsyncDisposable", state);
        Assert.Contains("private Task? _pollTask", state);
        Assert.Contains("private Task? _snapshotRefreshTask", state);
        Assert.Contains("private Task? _liveNotifyTask", state);
        Assert.Contains("await Task.WhenAll(tasks)", state);
        Assert.DoesNotContain("Task.Run", state);
        Assert.Contains("@implements IAsyncDisposable", component);
        Assert.Contains("await Dashboard.StopAsync()", component);
    }

    [Fact]
    public void IngestionDashboard_KeepsLifecycleProjectionSelectionAndPresentationSeparated()
    {
        var lifecycle = Read("src/MediaEngine.Web/Services/Integration/IngestionLiveDashboardState.cs");
        var projection = Read("src/MediaEngine.Web/Services/Integration/IngestionLiveDashboardState.Projection.cs");
        var markup = Read("src/MediaEngine.Web/Components/Settings/IngestionLiveDashboard.razor");
        var codeBehind = Read("src/MediaEngine.Web/Components/Settings/IngestionLiveDashboard.razor.cs");
        var selection = Read("src/MediaEngine.Web/Components/Settings/IngestionDashboardSelectionState.cs");
        var css = Read("src/MediaEngine.Web/Components/Settings/IngestionTasksTab.razor.css");

        Assert.Contains("public sealed partial class IngestionLiveDashboardState", lifecycle);
        Assert.Contains("public static IReadOnlyList<IngestionDashboardStage> BuildStages", projection);
        Assert.DoesNotContain("public static IReadOnlyList<IngestionDashboardStage> BuildStages", lifecycle);
        Assert.DoesNotContain("@code", markup);
        Assert.Contains("public partial class IngestionLiveDashboard", codeBehind);
        Assert.Contains("public sealed class IngestionDashboardSelectionState", selection);
        Assert.Contains("library-update-stage-cards", css);
        Assert.Contains("library-update-stage-detail-panel", css);

        var retiredCssFamilies = new[]
        {
            "library-update-batch-head-chip",
            "library-update-progress-block",
            "library-update-stage-list",
            "library-update-step__node",
            "library-update-stage__bar"
        };
        Assert.All(retiredCssFamilies, family => Assert.DoesNotContain(family, css));
    }

    [Fact]
    public void ProviderPriorityTab_UsesCodeBehindAndSidebarControlledStages()
    {
        var markup = Read("src/MediaEngine.Web/Components/Settings/ProviderPriorityTab.razor");
        var codeBehind = Read("src/MediaEngine.Web/Components/Settings/ProviderPriorityTab.razor.cs");
        var settings = Read("src/MediaEngine.Web/Components/Pages/Settings.razor");

        Assert.DoesNotContain("@code", markup);
        Assert.Contains("public partial class ProviderPriorityTab", codeBehind);
        Assert.DoesNotContain("<ProviderStageSelector", markup);
        Assert.Contains("[Parameter] public string Subsection", codeBehind);
        Assert.Contains("<ProviderPriorityTab Subsection=\"@_activeSubsection\" />", settings);
    }

    private static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoRoot, relativePath));
}
