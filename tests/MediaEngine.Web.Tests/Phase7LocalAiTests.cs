namespace MediaEngine.Web.Tests;

public sealed class Phase7LocalAiTests
{
    [Fact]
    public void LocalAiOverview_RendersRealHealthAndResources()
    {
        var source = ReadRepoFile(@"src\MediaEngine.Web\Components\Settings\LocalAiSettingsTab.razor");

        Assert.Contains("GetAiStatusAsync", source, StringComparison.Ordinal);
        Assert.Contains("GetAiProfileAsync", source, StringComparison.Ordinal);
        Assert.Contains("GetResourceSnapshotAsync", source, StringComparison.Ordinal);
        Assert.Contains("GetEnrichmentProgressAsync", source, StringComparison.Ordinal);
        Assert.Contains("Local AI runs on this server", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ModelsTab_RendersEngineModelStatusesAndLifecycleActions()
    {
        var source = ReadRepoFile(@"src\MediaEngine.Web\Components\Settings\ModelsTab.razor");

        Assert.Contains("GetAiModelStatusesAsync", source, StringComparison.Ordinal);
        Assert.Contains("StartAiModelDownloadAsync", source, StringComparison.Ordinal);
        Assert.Contains("CancelAiModelDownloadAsync", source, StringComparison.Ordinal);
        Assert.Contains("LoadAiModelAsync", source, StringComparison.Ordinal);
        Assert.Contains("UnloadAiModelAsync", source, StringComparison.Ordinal);
        Assert.Contains("Configured, but lifecycle endpoints do not support this role", source, StringComparison.Ordinal);
        Assert.DoesNotContain("catalogue-only", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Task.Delay", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AiFeatures_LoadFromConfigAndExplainDependencies()
    {
        var source = ReadRepoFile(@"src\MediaEngine.Web\Components\Settings\AiFeaturesTab.razor");

        Assert.Contains("GetAiConfigAsync", source, StringComparison.Ordinal);
        Assert.Contains("SaveAiConfigAsync", source, StringComparison.Ordinal);
        Assert.Contains("Missing model", source, StringComparison.Ordinal);
        Assert.Contains("Hardware limited", source, StringComparison.Ordinal);
        Assert.Contains("Not connected", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ScheduleAndVocabulary_LoadAndSaveAiConfig()
    {
        var schedule = ReadRepoFile(@"src\MediaEngine.Web\Components\Settings\AiScheduleTab.razor");
        var vocabulary = ReadRepoFile(@"src\MediaEngine.Web\Components\Settings\VibeVocabularyTab.razor");

        Assert.Contains("GetAiConfigAsync", schedule, StringComparison.Ordinal);
        Assert.Contains("SaveAiConfigAsync", schedule, StringComparison.Ordinal);
        Assert.Contains("Last-run and next-run times are not shown", schedule, StringComparison.Ordinal);
        Assert.Contains("GetAiConfigAsync", vocabulary, StringComparison.Ordinal);
        Assert.Contains("SaveAiConfigAsync", vocabulary, StringComparison.Ordinal);
        Assert.Contains("Duplicate tag", vocabulary, StringComparison.Ordinal);
    }

    [Fact]
    public void Source_DoesNotReintroduceVaultWorkflow()
    {
        var localAi = ReadRepoFile(@"src\MediaEngine.Web\Components\Settings\LocalAiSettingsTab.razor");
        var models = ReadRepoFile(@"src\MediaEngine.Web\Components\Settings\ModelsTab.razor");

        Assert.DoesNotContain("/vault", localAi, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Vault", localAi, StringComparison.Ordinal);
        Assert.DoesNotContain("/vault", models, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Vault", models, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            relativePath)));
}
