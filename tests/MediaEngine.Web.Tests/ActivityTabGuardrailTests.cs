using System.IO;

namespace MediaEngine.Web.Tests;

public sealed class ActivityTabGuardrailTests
{
    [Fact]
    public void ActivityTab_UsesEngineLedgerInsteadOfSampleData()
    {
        var source = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Settings\ActivityTab.razor"));

        Assert.Contains("GetActivityStatsAsync", source, StringComparison.Ordinal);
        Assert.Contains("GetRecentActivityAsync", source, StringComparison.Ordinal);
        Assert.Contains("GetActivityByTypesAsync", source, StringComparison.Ordinal);
        Assert.Contains("TriggerPruneAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_sampleEntries", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.Delay", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Dune.m4b moved to library", source, StringComparison.Ordinal);
    }

    private static string GetRepoFilePath(string relativePath) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relativePath));
}
