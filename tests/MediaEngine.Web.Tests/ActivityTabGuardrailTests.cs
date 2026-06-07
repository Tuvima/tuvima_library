using System.IO;
using System.Text.RegularExpressions;

namespace MediaEngine.Web.Tests;

public sealed class ActivityTabGuardrailTests
{
    [Fact]
    public void ActivityTab_ComposesCentralizedActivitySurfaces()
    {
        var tab = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Settings\ActivityTab.razor"));
        var batches = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Activity\ActivityBatchExplorer.razor"));
        var batchCss = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Activity\ActivityBatchExplorer.razor.css"));
        var group = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Activity\ActivityMediaTypeAuditGroup.razor"));
        var detail = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Activity\ActivityTitleAuditDetail.razor"));
        var people = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Activity\ActivityPeopleAudit.razor"));
        var events = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Activity\ActivityEventsLedger.razor"));
        var maintenance = File.ReadAllText(GetRepoFilePath(@"src\MediaEngine.Web\Components\Activity\ActivityMaintenancePanel.razor"));

        Assert.Contains("<ActivityBatchExplorer", tab, StringComparison.Ordinal);
        Assert.Contains("<ActivityPeopleAudit", tab, StringComparison.Ordinal);
        Assert.Contains("<ActivityEventsLedger", tab, StringComparison.Ordinal);
        Assert.Contains("<ActivityMaintenancePanel", tab, StringComparison.Ordinal);
        Assert.DoesNotContain("GetActivity", tab, StringComparison.Ordinal);
        Assert.DoesNotContain("SELECT", tab, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("GetActivityBatchesAsync", batches, StringComparison.Ordinal);
        Assert.Contains("GetActivityBatchGroupsAsync", batches, StringComparison.Ordinal);
        Assert.Contains("GetActivityBatchItemsAsync", batches, StringComparison.Ordinal);
        Assert.Contains("GetActivityBatchItemDetailAsync", batches, StringComparison.Ordinal);
        Assert.Contains("AppFilterBar", batches, StringComparison.Ordinal);
        Assert.Contains("AppExpandableAuditTable", batches, StringComparison.Ordinal);
        Assert.Contains("AppCompactPager", batches, StringComparison.Ordinal);
        Assert.Contains("Label=\"Batch ID\"", batches, StringComparison.Ordinal);
        Assert.Contains("Label=\"Started\"", batches, StringComparison.Ordinal);
        Assert.Contains("Label=\"Media\"", batches, StringComparison.Ordinal);
        Assert.Contains("Label=\"Total\"", batches, StringComparison.Ordinal);
        Assert.Contains("Label=\"Duration\"", batches, StringComparison.Ordinal);
        Assert.Contains("Label=\"Status\"", batches, StringComparison.Ordinal);
        Assert.DoesNotContain("Label=\"Source\"", batches, StringComparison.Ordinal);
        Assert.Contains("table-layout: fixed", batchCss, StringComparison.Ordinal);

        Assert.Contains("Label=\"Title\"", group, StringComparison.Ordinal);
        Assert.Contains("Label=\"Provider\"", group, StringComparison.Ordinal);
        Assert.Contains("Label=\"QID\"", group, StringComparison.Ordinal);
        Assert.Contains("Label=\"People\"", group, StringComparison.Ordinal);
        Assert.Contains("Label=\"Duration\"", group, StringComparison.Ordinal);
        Assert.DoesNotContain("Books T", group, StringComparison.Ordinal);
        Assert.DoesNotContain("Catalog Match", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("Apple Books", detail, StringComparison.Ordinal);

        Assert.Contains("GetActivityPeopleAsync", people, StringComparison.Ordinal);
        Assert.Contains("AppFilterBar", people, StringComparison.Ordinal);
        Assert.Contains("AppExpandableAuditTable", people, StringComparison.Ordinal);

        Assert.Contains("GetRecentActivityAsync", events, StringComparison.Ordinal);
        Assert.Contains("GetActivityByTypesAsync", events, StringComparison.Ordinal);
        Assert.Contains("GetActivityByRunIdAsync", events, StringComparison.Ordinal);
        Assert.Contains("QueryHelpers.ParseQuery", events, StringComparison.Ordinal);
        Assert.Contains("\"runId\"", events, StringComparison.Ordinal);
        Assert.Contains("\"batchId\"", events, StringComparison.Ordinal);

        Assert.Contains("GetActivityStatsAsync", maintenance, StringComparison.Ordinal);
        Assert.Contains("TriggerPruneAsync", maintenance, StringComparison.Ordinal);

        foreach (var source in new[] { tab, batches, people, events, maintenance })
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
