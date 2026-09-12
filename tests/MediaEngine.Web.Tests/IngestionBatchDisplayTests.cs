using MediaEngine.Contracts.Activity;
using MediaEngine.Web.Components.Settings;

namespace MediaEngine.Web.Tests;

public sealed class IngestionBatchDisplayTests
{
    [Fact]
    public void ZeroAdditionBatch_ExplainsTheOutcomeWithoutMediaNavigation()
    {
        var batch = new ActivityBatchSummaryDto
        {
            BatchId = Guid.Parse("83000000-0000-0000-0000-000000000001"),
            Status = "completed",
            FilesDiscoveredCount = 38,
            FilesProcessedCount = 38,
            AddedGroupCount = 0,
        };

        Assert.False(IngestionBatchDisplay.HasMedia(batch));
        Assert.Equal("38 files checked", IngestionBatchDisplay.FilesChecked(batch));
        Assert.Equal("No new media added", IngestionBatchDisplay.AddedOutcome(batch));
        Assert.Equal("The scan completed and found no new media to add.", IngestionBatchDisplay.Summary(batch));
    }

    [Fact]
    public void PartialBatch_KeepsDiscoveredAndProcessedCountsDistinct()
    {
        var batch = new ActivityBatchSummaryDto
        {
            FilesDiscoveredCount = 38,
            FilesProcessedCount = 12,
        };

        Assert.Equal("12 of 38 files checked", IngestionBatchDisplay.FilesChecked(batch));
    }

    [Fact]
    public void BatchWithMedia_UsesTheCanonicalDetailRoute()
    {
        var batch = new ActivityBatchSummaryDto
        {
            BatchId = Guid.Parse("83000000-0000-0000-0000-000000000001"),
            AddedGroupCount = 5,
        };

        Assert.True(IngestionBatchDisplay.HasMedia(batch));
        Assert.Equal("5 library items added", IngestionBatchDisplay.AddedOutcome(batch));
        Assert.Equal(
            "/operations/ingestion?runId=83000000-0000-0000-0000-000000000001&view=all",
            IngestionBatchDisplay.MediaHref(batch));
    }
}
