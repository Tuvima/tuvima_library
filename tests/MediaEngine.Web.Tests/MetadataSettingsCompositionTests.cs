using System.IO;

namespace MediaEngine.Web.Tests;

public sealed class MetadataSettingsCompositionTests
{
    [Fact]
    public void MetadataSettings_ComposesTheThreeProductSurfaces()
    {
        var page = Read("src/MediaEngine.Web/Components/Settings/MetadataSettingsPage.razor");
        var state = Read("src/MediaEngine.Web/Services/Integration/MetadataSettingsStateService.cs");

        Assert.Contains("<SettingsSubsectionNav", page, StringComparison.Ordinal);
        Assert.Contains("ProvidersOverview()", page, StringComparison.Ordinal);
        Assert.Contains("EnrichmentOverview()", page, StringComparison.Ordinal);
        Assert.Contains("CanonicalOverview()", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Source Priority", page, StringComparison.Ordinal);
        Assert.Contains("[\"Books\", \"Audiobooks\", \"Comics\", \"Movies\", \"TV\", \"Music\"]", state, StringComparison.Ordinal);
        Assert.Contains("\"artwork\", \"Artwork\"", state, StringComparison.Ordinal);
        Assert.Contains("\"lyrics\", \"Lyrics\"", state, StringComparison.Ordinal);
        Assert.Contains("\"subtitles\", \"Subtitles\"", state, StringComparison.Ordinal);
        Assert.Contains("\"people\", \"People\"", state, StringComparison.Ordinal);
        Assert.DoesNotContain("\"ratings\", \"Ratings\"", state, StringComparison.Ordinal);
        Assert.False(File.Exists(PathFor("src/MediaEngine.Web/Components/Settings/ProviderPriorityTab.razor")));
        Assert.False(File.Exists(PathFor("src/MediaEngine.Web/Components/Settings/ProviderPrioritySurface.razor")));
        Assert.False(File.Exists(PathFor("src/MediaEngine.Web/Components/Settings/ProviderHealthSurface.razor")));
    }

    [Fact]
    public void ProviderHealth_IsEmbeddedAndUsesRecordedEngineState()
    {
        var page = Read("src/MediaEngine.Web/Components/Settings/MetadataSettingsPage.razor");
        var state = Read("src/MediaEngine.Web/Services/Integration/MetadataSettingsStateService.cs");

        Assert.Contains("TestProviderAsync", page, StringComparison.Ordinal);
        Assert.Contains("GetProviderStatusAsync", state, StringComparison.Ordinal);
        Assert.Contains("HealthLabel(status)", state, StringComparison.Ordinal);
        Assert.DoesNotContain("98%", page, StringComparison.Ordinal);
        Assert.DoesNotContain("209 ms", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Enrichment_OwnsTheRealRefreshSchedule()
    {
        var enrichment = Read("src/MediaEngine.Web/Components/Settings/MetadataSettingsPage.razor");
        var schedule = Read("src/MediaEngine.Web/Components/Settings/EnrichmentRefreshSchedulePanel.razor");

        Assert.Contains("<EnrichmentRefreshSchedulePanel", enrichment, StringComparison.Ordinal);
        Assert.Contains("GetEnrichmentRefreshScheduleAsync", schedule, StringComparison.Ordinal);
        Assert.Contains("QueueEnrichmentRefreshNowAsync", schedule, StringComparison.Ordinal);
    }

    [Fact]
    public void ArtworkSettings_ProtectManualChoicesAndExposeOnlyRealPolicy()
    {
        var page = Read("src/MediaEngine.Web/Components/Settings/MetadataSettingsPage.razor");

        Assert.Contains("Initial Artwork", page, StringComparison.Ordinal);
        Assert.Contains("Additional Artwork", page, StringComparison.Ordinal);
        Assert.Contains("Fill missing artwork automatically", page, StringComparison.Ordinal);
        Assert.Contains("Keep manually selected artwork", page, StringComparison.Ordinal);
        Assert.Contains("always protected from automation", page, StringComparison.Ordinal);
        Assert.Contains("Use existing local artwork", page, StringComparison.Ordinal);
        Assert.Contains("Built in", page, StringComparison.Ordinal);
    }

    [Fact]
    public void LibraryOverview_ExplainsTheConfiguredMetadataPipeline()
    {
        var libraries = Read("src/MediaEngine.Web/Components/Settings/LibrariesTab.razor");
        var summary = Read("src/MediaEngine.Web/Components/Settings/MetadataPipelineStrip.razor");

        Assert.Contains("<MetadataPipelineStrip", libraries, StringComparison.Ordinal);
        Assert.Contains("Stage 1 — Metadata Provider", summary, StringComparison.Ordinal);
        Assert.Contains("Stage 2 — Canonical Identity", summary, StringComparison.Ordinal);
        Assert.Contains("Stage 3 — Enrichment", summary, StringComparison.Ordinal);
        Assert.Contains("Manage Metadata Settings", summary, StringComparison.Ordinal);
        Assert.Contains("Local metadata only", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void NeedsReview_KeepsItsWorkflowBehindASimplerResponsiveSummary()
    {
        var review = Read("src/MediaEngine.Web/Components/Settings/SettingsReviewQueueTab.razor");
        var css = Read("src/MediaEngine.Web/Components/Settings/SettingsReviewQueueTab.razor.css");

        Assert.Contains("items need review", review, StringComparison.Ordinal);
        Assert.Contains("settings-review-table-header", review, StringComparison.Ordinal);
        Assert.Contains("Why it is here", review, StringComparison.Ordinal);
        Assert.Contains("Dismiss from review", review, StringComparison.Ordinal);
        Assert.DoesNotContain("settings-review-inspector", review, StringComparison.Ordinal);
        Assert.Contains("OpenReviewEditorAsync", review, StringComparison.Ordinal);
        Assert.Contains("DismissAsync", review, StringComparison.Ordinal);
        Assert.Contains("@media(max-width:900px)", css, StringComparison.Ordinal);
        Assert.Contains("flex-wrap:wrap", css, StringComparison.Ordinal);
        Assert.DoesNotContain("min-width: 660px", css, StringComparison.Ordinal);
    }

    private static string Read(string relativePath) => File.ReadAllText(PathFor(relativePath));

    private static string PathFor(string relativePath) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relativePath));
}
