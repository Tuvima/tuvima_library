using MediaEngine.Web.Components.Settings;
using MediaEngine.Web.Models.ViewDTOs;
using MediaEngine.Web.Services.Integration;

namespace MediaEngine.Web.Tests;

public sealed class IngestionDashboardSelectionStateTests
{
    [Fact]
    public void Synchronize_SelectsLatestBatchAndDefaultStage()
    {
        var batch = new IngestionOperationsBatchViewModel { BatchId = Guid.NewGuid() };
        var stage = Stage("scan", 1);
        var state = new IngestionDashboardSelectionState();

        state.Synchronize([batch], [stage], StageKey, stages => stages[0]);

        Assert.Equal(batch.BatchId, state.BatchId);
        Assert.Equal("1", state.StageKey);
    }

    [Fact]
    public void Synchronize_ReplacesAStageThatDisappearedFromLiveSnapshot()
    {
        var state = new IngestionDashboardSelectionState();
        state.SelectStage("missing");
        var replacement = Stage("retail", 2);

        state.Synchronize([], [replacement], StageKey, stages => stages[0]);

        Assert.Equal("2", state.StageKey);
    }

    [Fact]
    public void SelectBatch_ClearsStageUntilTheNewBatchIsSynchronized()
    {
        var state = new IngestionDashboardSelectionState();
        state.SelectStage("1");
        var batchId = Guid.NewGuid();

        state.SelectBatch(batchId);

        Assert.Equal(batchId, state.BatchId);
        Assert.Null(state.StageKey);
    }

    private static string StageKey(IngestionDashboardStage stage) => stage.StageNumber.ToString();

    private static IngestionDashboardStage Stage(string key, int number) =>
        new(key, key, key, string.Empty, 0, 0, 0, string.Empty, 0, false, 0, 0, 0, false, number);
}
