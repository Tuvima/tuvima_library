using MediaEngine.Ingestion.Services;
using MediaEngine.Ingestion.Tests.Helpers;
using MediaEngine.Storage.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaEngine.Ingestion.Tests;

public sealed class WritebackConfigStateTests : IDisposable
{
    private readonly string _configDirectory = Path.Combine(
        Path.GetTempPath(),
        $"tuvima-writeback-state-{Guid.NewGuid():N}");

    public WritebackConfigStateTests() => Directory.CreateDirectory(_configDirectory);

    public void Dispose()
    {
        try { Directory.Delete(_configDirectory, recursive: true); } catch { }
    }

    [Fact]
    public async Task FileChange_UsesOwnedTimerToStagePendingDiff()
    {
        var loader = CreateLoader(["title"]);
        using var state = new WritebackConfigState(
            loader,
            NullLogger<WritebackConfigState>.Instance);
        var changed = new TaskCompletionSource<IReadOnlyList<WritebackFieldDiff>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        state.PendingChanged += diff => changed.TrySetResult(diff);
        loader.WritebackFields = new WritebackFieldsConfiguration { Books = ["title", "author"] };

        RaiseFileChanged(state);

        var diff = await changed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Contains(diff, item => item.MediaType == "Books");
        Assert.True(state.HasPendingDiff);
    }

    [Fact]
    public async Task Dispose_CancelsPendingReloadAndPreventsNotifications()
    {
        var loader = CreateLoader(["title"]);
        var state = new WritebackConfigState(
            loader,
            NullLogger<WritebackConfigState>.Instance);
        var notificationCount = 0;
        state.PendingChanged += _ => Interlocked.Increment(ref notificationCount);
        loader.WritebackFields = new WritebackFieldsConfiguration { Books = ["title", "author"] };

        RaiseFileChanged(state);
        state.Dispose();
        await Task.Delay(300);

        Assert.Equal(0, Volatile.Read(ref notificationCount));
        Assert.False(state.HasPendingDiff);
    }

    [Fact]
    public void BackgroundWork_DoesNotUseDetachedTaskRun()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var ingestionSource = File.ReadAllText(Path.Combine(repoRoot, "src", "MediaEngine.Ingestion", "IngestionEngine.cs"));
        var configSource = File.ReadAllText(Path.Combine(repoRoot, "src", "MediaEngine.Ingestion", "Services", "WritebackConfigState.cs"));

        Assert.DoesNotContain("Task.Run", ingestionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.Run", configSource, StringComparison.Ordinal);
        Assert.Contains("TrackBackgroundTask", ingestionSource, StringComparison.Ordinal);
        Assert.Contains("_reloadTimer", configSource, StringComparison.Ordinal);
    }

    private StubConfigurationLoader CreateLoader(IReadOnlyList<string> bookFields) => new()
    {
        ConfigDirectoryPath = _configDirectory,
        WritebackFields = new WritebackFieldsConfiguration { Books = [.. bookFields] },
    };

    private static void RaiseFileChanged(WritebackConfigState state)
    {
        var method = typeof(WritebackConfigState).GetMethod(
            "OnFileChanged",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(state,
        [
            state,
            new FileSystemEventArgs(
                WatcherChangeTypes.Changed,
                Path.GetTempPath(),
                "writeback-fields.json"),
        ]);
    }
}
