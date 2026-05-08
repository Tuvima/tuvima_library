namespace MediaEngine.Web.Tests;

public sealed class IngestionOperationsPageGuardrailTests
{
    [Fact]
    public void IngestionTab_UsesOperationsSnapshotAndLiveRefresh()
    {
        var source = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Settings\IngestionTasksTab.razor"));

        Assert.Contains("Library Operations", source, StringComparison.Ordinal);
        Assert.Contains("GetIngestionOperationsSnapshotAsync", source, StringComparison.Ordinal);
        Assert.Contains("StartSignalRAsync", source, StringComparison.Ordinal);
        Assert.Contains("StateContainer.BatchProgress", source, StringComparison.Ordinal);
        Assert.Contains("StateContainer.IngestionProgress", source, StringComparison.Ordinal);
        Assert.Contains("Task.Delay(delay, ct)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Dune Messiah", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TODO: Replace sample monitor state", source, StringComparison.Ordinal);
        Assert.DoesNotContain("private readonly CurrentRun _currentRun", source, StringComparison.Ordinal);
    }

    private static string GetRepoFilePath(string relativePath) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relativePath));
}
