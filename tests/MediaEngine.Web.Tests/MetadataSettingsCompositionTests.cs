using System.IO;

namespace MediaEngine.Web.Tests;

public sealed class MetadataSettingsCompositionTests
{
    [Fact]
    public void MetadataSubsections_RenderOnlyTheirOwnedSurface()
    {
        var tab = Read("src/MediaEngine.Web/Components/Settings/ProviderPriorityTab.razor");
        var codeBehind = Read("src/MediaEngine.Web/Components/Settings/ProviderPriorityTab.razor.cs");

        Assert.Contains("<ProviderEnrichmentSurface", tab, StringComparison.Ordinal);
        Assert.Contains("<ProviderPrioritySurface", tab, StringComparison.Ordinal);
        Assert.Contains("<ProviderHealthSurface", tab, StringComparison.Ordinal);
        Assert.Contains("IsProvidersSurface", tab, StringComparison.Ordinal);
        Assert.Contains("IsHealthSurface", codeBehind, StringComparison.Ordinal);
        Assert.Contains("if (IsProvidersSurface)", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderHealth_UsesRecordedEngineDataWithoutInventedMetrics()
    {
        var health = Read("src/MediaEngine.Web/Components/Settings/ProviderHealthSurface.razor");

        Assert.Contains("GetProviderHealthAsync", health, StringComparison.Ordinal);
        Assert.Contains("GetProviderStatusAsync", health, StringComparison.Ordinal);
        Assert.Contains("TestProviderAsync", health, StringComparison.Ordinal);
        Assert.Contains("No check recorded", health, StringComparison.Ordinal);
        Assert.Contains("Response time is shown only after", health, StringComparison.Ordinal);
        Assert.DoesNotContain("98%", health, StringComparison.Ordinal);
        Assert.DoesNotContain("209 ms", health, StringComparison.Ordinal);
    }

    [Fact]
    public void Enrichment_OwnsTheRealRefreshSchedule()
    {
        var enrichment = Read("src/MediaEngine.Web/Components/Settings/ProviderEnrichmentSurface.razor");
        var schedule = Read("src/MediaEngine.Web/Components/Settings/EnrichmentRefreshSchedulePanel.razor");

        Assert.Contains("<EnrichmentRefreshSchedulePanel", enrichment, StringComparison.Ordinal);
        Assert.Contains("GetEnrichmentRefreshScheduleAsync", schedule, StringComparison.Ordinal);
        Assert.Contains("QueueEnrichmentRefreshNowAsync", schedule, StringComparison.Ordinal);
    }

    [Fact]
    public void NeedsReview_KeepsItsWorkflowBehindASimplerResponsiveSummary()
    {
        var review = Read("src/MediaEngine.Web/Components/Settings/SettingsReviewQueueTab.razor");
        var css = Read("src/MediaEngine.Web/Components/Settings/SettingsReviewQueueTab.razor.css");

        Assert.Contains("\"Needing review\"", review, StringComparison.Ordinal);
        Assert.Contains("\"High priority\"", review, StringComparison.Ordinal);
        Assert.Contains("\"Oldest waiting\"", review, StringComparison.Ordinal);
        Assert.Contains("OpenReviewEditorAsync", review, StringComparison.Ordinal);
        Assert.Contains("DismissAsync", review, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 720px)", css, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: minmax(0, 1fr) auto", css, StringComparison.Ordinal);
        Assert.DoesNotContain("min-width: 660px", css, StringComparison.Ordinal);
    }

    private static string Read(string relativePath) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relativePath)));
}
