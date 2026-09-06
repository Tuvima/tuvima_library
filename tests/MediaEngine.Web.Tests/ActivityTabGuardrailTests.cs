using System.IO;
using System.Text.RegularExpressions;
using MediaEngine.Web.Components.Activity;

namespace MediaEngine.Web.Tests;

public sealed class ActivityTabGuardrailTests
{
    [Theory]
    [InlineData("QidResolved")]
    [InlineData("RetailMatched")]
    [InlineData("ReadyWithoutUniverse")]
    public void ActivityDisplay_TreatsLibraryReadyStatusesAsComplete(string status)
    {
        Assert.Equal("Complete", ActivityDisplay.StatusText(status, "Complete"));
    }

    [Fact]
    public void ActivityTab_ComposesCentralizedActivitySurfaces()
    {
        var tab = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Settings\ActivityTab.razor"));
        var settings = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Pages\Settings.razor"));
        var batches = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Activity\ActivityBatchExplorer.razor"));
        var batchCss = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Activity\ActivityBatchExplorer.razor.css"));
        var inspector = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Activity\ActivityBatchInspector.razor"));
        var maintenance = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Activity\ActivityMaintenancePanel.razor"));

        Assert.Contains("<ActivityBatchExplorer", tab, StringComparison.Ordinal);
        Assert.Contains("<ActivityMaintenancePanel", tab, StringComparison.Ordinal);
        Assert.DoesNotContain("<AppTabs", tab, StringComparison.Ordinal);
        Assert.DoesNotContain("Timeline", tab, StringComparison.Ordinal);
        Assert.DoesNotContain("ActivityEventsLedger", tab, StringComparison.Ordinal);
        Assert.DoesNotContain("ActivityPeopleAudit", tab, StringComparison.Ordinal);
        Assert.Contains("settings-operations-navigation", settings, StringComparison.Ordinal);
        Assert.Contains("Activity &amp; Audit", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("GetActivity", tab, StringComparison.Ordinal);
        Assert.DoesNotContain("SELECT", tab, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("GetActivityBatchesAsync", batches, StringComparison.Ordinal);
        Assert.Contains("GetActivityBatchAsync", batches, StringComparison.Ordinal);
        Assert.Contains("GetActivityBatchInsightsAsync", inspector, StringComparison.Ordinal);
        Assert.Contains("GroupBy(operation => operation.BatchId)", batches, StringComparison.Ordinal);
        Assert.DoesNotContain("AppFilterBar", batches, StringComparison.Ordinal);
        Assert.Contains("activity-audit__toolbar", batches, StringComparison.Ordinal);
        Assert.Contains("activity-operation", batches, StringComparison.Ordinal);
        Assert.Contains("ActivityBatchInspector", batches, StringComparison.Ordinal);
        Assert.Contains("Technical Log", inspector, StringComparison.Ordinal);
        Assert.Contains("GetActivityBatchEventsAsync", inspector, StringComparison.Ordinal);
        Assert.DoesNotContain("ActivityEventList", inspector, StringComparison.Ordinal);
        Assert.DoesNotContain("Important changes", batches, StringComparison.Ordinal);
        Assert.Contains("AppCompactPager", batches, StringComparison.Ordinal);
        Assert.Contains("Label=\"Date range\"", batches, StringComparison.Ordinal);
        Assert.DoesNotContain("Label=\"Activity type\"", batches, StringComparison.Ordinal);
        Assert.DoesNotContain("Label=\"Media type\"", batches, StringComparison.Ordinal);
        Assert.DoesNotContain("activity-audit__lenses", batches, StringComparison.Ordinal);
        Assert.DoesNotContain("InspectionButton", batches, StringComparison.Ordinal);
        Assert.Contains("OperationSubtitle", batches, StringComparison.Ordinal);
        Assert.Contains("Label=\"All activity\"", batches, StringComparison.Ordinal);
        Assert.Contains("GetActivityBatchAsync(runId)", batches, StringComparison.Ordinal);
        Assert.Contains("OnItemPageChanged", inspector, StringComparison.Ordinal);
        Assert.Contains("ActivityBatchItemInspector", inspector, StringComparison.Ordinal);
        Assert.Contains("Provider requests", inspector, StringComparison.Ordinal);
        Assert.DoesNotContain("activity-audit__summary", batches, StringComparison.Ordinal);
        Assert.Contains("@media(max-width:980px)", batchCss, StringComparison.Ordinal);
        Assert.Contains(".activity-ledger__head", batchCss, StringComparison.Ordinal);
        Assert.Contains("<details class=\"activity-page__advanced\"", tab, StringComparison.Ordinal);

        Assert.Contains("GetActivityStatsAsync", maintenance, StringComparison.Ordinal);
        Assert.Contains("TriggerPruneAsync", maintenance, StringComparison.Ordinal);

        foreach (var source in new[] { tab, batches, inspector, maintenance })
        {
            Assert.DoesNotContain("_sampleEntries", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Task.Delay", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Dune.m4b moved to library", source, StringComparison.Ordinal);
            Assert.False(Regex.IsMatch(source, @"(?im)^\s*SELECT\s+"), "Razor components must not contain direct SQL.");
        }
    }

    private static string GetRepoFilePath(string relativePath) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relativePath));
}
