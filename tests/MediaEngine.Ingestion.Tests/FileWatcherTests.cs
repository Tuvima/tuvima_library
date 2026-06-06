using System.Reflection;
using MediaEngine.Ingestion;
using MediaEngine.Ingestion.Models;

namespace MediaEngine.Ingestion.Tests;

public sealed class FileWatcherTests
{
    [Fact]
    public void OnError_RecordsStatusAndRaisesWatcherError()
    {
        using var watcher = new FileWatcher();
        FileWatcherErrorEvent? observed = null;
        watcher.WatcherError += (_, evt) => observed = evt;

        var onError = typeof(FileWatcher).GetMethod(
            "OnError",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(onError);

        onError.Invoke(
            watcher,
            [watcher, new ErrorEventArgs(new InternalBufferOverflowException("buffer overflow"))]);

        Assert.Equal(1, watcher.ErrorCount);
        Assert.NotNull(watcher.LastErrorAt);
        Assert.Equal("buffer_overflow", watcher.LastErrorKind);
        Assert.Equal("buffer overflow", watcher.LastErrorMessage);
        Assert.NotNull(observed);
        Assert.True(observed.IsBufferOverflow);
        Assert.Equal("buffer_overflow", observed.Kind);
    }
}
