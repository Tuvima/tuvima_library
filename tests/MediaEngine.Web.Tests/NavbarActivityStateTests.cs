using MediaEngine.Web.Services.Integration;

namespace MediaEngine.Web.Tests;

public sealed class NavbarActivityStateTests
{
    [Fact]
    public void ModelDownloadActivity_IsRemovedWhenDownloadCompletes()
    {
        var state = new UniverseStateContainer();

        state.PushModelDownloadProgress(new ModelDownloadProgressEvent("filename_parser", 42, 420, 1000));
        Assert.Single(state.ModelDownloadActivity);

        state.PushModelDownloadProgress(new ModelDownloadProgressEvent("filename_parser", 100, 1000, 1000));
        Assert.Empty(state.ModelDownloadActivity);
    }

    [Fact]
    public void DurableOperationActivity_OnlyRetainsActiveStatuses()
    {
        var state = new UniverseStateContainer();
        var operationId = Guid.NewGuid();
        var running = new MediaOperationChangedEvent(
            operationId,
            "ai.tldr",
            "ai",
            "running",
            "parsing",
            35,
            1,
            0,
            DateTimeOffset.UtcNow);

        state.PushMediaOperationChanged(running);
        Assert.Collection(state.MediaOperationActivity, item => Assert.Equal(operationId, item.Id));

        state.PushMediaOperationChanged(running with { Status = "succeeded", ProgressPercent = 100 });
        Assert.Empty(state.MediaOperationActivity);
    }
}
