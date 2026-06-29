using MediaEngine.Web.Services.Playback;

namespace MediaEngine.Web.Tests;

public sealed class PlaybackPrimitiveTests
{
    [Theory]
    [InlineData("Music", PlaybackExperience.Music)]
    [InlineData("Audio", PlaybackExperience.Music)]
    [InlineData("Audiobooks", PlaybackExperience.Audiobook)]
    [InlineData("M4B", PlaybackExperience.Audiobook)]
    [InlineData("Movies", PlaybackExperience.Video)]
    [InlineData("TV", PlaybackExperience.Video)]
    [InlineData("MKV", PlaybackExperience.Video)]
    [InlineData("Unknown", PlaybackExperience.Music)]
    public void MediaKindClassifier_MapsKnownStringsToPlaybackExperience(string mediaType, PlaybackExperience expected)
    {
        Assert.Equal(expected, MediaKindClassifier.Classify(mediaType));
    }

    [Fact]
    public void PlaybackQueue_CachesUpcomingAndKeepsHistoryIndexStable()
    {
        var queue = new PlaybackQueue();
        var current = Item("Current");
        var next = Item("Next");
        var last = Item("Last");

        queue.Replace([current, next, last], startIndex: 0);

        Assert.Equal([next, last], queue.Upcoming);

        queue.MoveTo(1);

        Assert.Equal(next, queue.Current);
        Assert.Equal([last], queue.Upcoming);

        queue.ClearUpcoming();

        Assert.Equal([current, next], queue.Items);
        Assert.Empty(queue.Upcoming);
    }

    [Fact]
    public void PlaybackQueue_AudiobookReplacementUsesSingleItemRule()
    {
        var queue = new PlaybackQueue();
        queue.Replace([Item("Song 1"), Item("Song 2")], startIndex: 0);

        queue.ReplaceWithSingleAudiobook(Item("Book") with { MediaType = "M4B" });

        Assert.Single(queue.Items);
        Assert.Equal("Book", queue.Current?.Title);
        Assert.Equal("Audiobooks", queue.Current?.MediaType);
    }

    [Fact]
    public void PlaybackStateMachine_TracksTransportPhases()
    {
        var machine = new PlaybackStateMachine();

        Assert.Equal(PlaybackPhase.Playing, machine.Transition(PlaybackCommandKind.TogglePlay));
        machine.SetTransportState(isPlaying: false, needsUserGesture: false, error: null);
        Assert.Equal(PlaybackPhase.Paused, machine.Phase);
        machine.SetTransportState(isPlaying: null, needsUserGesture: true, error: null);
        Assert.Equal(PlaybackPhase.NeedsGesture, machine.Phase);
        machine.SetTransportState(isPlaying: null, needsUserGesture: false, error: "Failed");
        Assert.Equal(PlaybackPhase.Error, machine.Phase);
        machine.SetEnded();
        Assert.Equal(PlaybackPhase.Ended, machine.Phase);
    }

    [Fact]
    public void PlaybackClientContext_NormalizesWebDefaultsWithoutOldDashboardDeviceId()
    {
        var context = new PlaybackClientContext("", "", "", "", "").Normalize();

        Assert.Equal("web", context.DeviceId);
        Assert.Equal("web", context.Client);
        Assert.DoesNotContain("web-dashboard", context.DeviceId, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ListenPlaybackClientSettings_NormalizeClampsUnsafeValues()
    {
        var settings = new ListenPlaybackClientSettings
        {
            PopupWidth = 10,
            PopupHeight = 10000,
            AudioObserverIntervalMilliseconds = 1,
            SeekToleranceSeconds = 100,
            VolumeStep = 2,
            HeartbeatIntervalSeconds = 0,
            PendingTransportCommandLimit = 999,
            DefaultVolume = 2,
        }.Normalize();

        Assert.Equal(280, settings.PopupWidth);
        Assert.Equal(1400, settings.PopupHeight);
        Assert.Equal(100, settings.AudioObserverIntervalMilliseconds);
        Assert.Equal(10, settings.SeekToleranceSeconds);
        Assert.Equal(0.5d, settings.VolumeStep);
        Assert.Equal(1, settings.HeartbeatIntervalSeconds);
        Assert.Equal(256, settings.PendingTransportCommandLimit);
        Assert.Equal(1d, settings.DefaultVolume);
    }

    [Fact]
    public void PlaybackJavaScript_UsesV2StorageKeysAndNoLegacyStateFallback()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "src/MediaEngine.Web/wwwroot/app.js"));

        Assert.Contains("tuvima.playback.v2.state", script, StringComparison.Ordinal);
        Assert.Contains("tuvima.playback.v2.command", script, StringComparison.Ordinal);
        Assert.Contains("tuvima.playback.v2.device-id", script, StringComparison.Ordinal);
        Assert.DoesNotContain("listen-playback-state", script, StringComparison.Ordinal);
        Assert.DoesNotContain("listen-playback-command", script, StringComparison.Ordinal);
    }

    [Fact]
    public void ListenSurfaces_UseSharedTransportControls()
    {
        var root = FindRepoRoot();
        var bar = File.ReadAllText(Path.Combine(root, "src/MediaEngine.Web/Components/Listen/ListenNowPlayingBar.razor"));
        var panel = File.ReadAllText(Path.Combine(root, "src/MediaEngine.Web/Components/Listen/ListenNowPlayingPanel.razor"));
        var popup = File.ReadAllText(Path.Combine(root, "src/MediaEngine.Web/Components/Pages/ListenPlayerPopupPage.razor"));
        var shared = File.ReadAllText(Path.Combine(root, "src/MediaEngine.Web/Components/Listen/ListenTransportControls.razor"));

        Assert.Contains("<ListenTransportControls", bar, StringComparison.Ordinal);
        Assert.Contains("<ListenTransportControls", panel, StringComparison.Ordinal);
        Assert.Contains("<ListenTransportControls", popup, StringComparison.Ordinal);
        Assert.Contains("AriaLabel=\"Play or pause\"", shared, StringComparison.Ordinal);
        Assert.Contains("AriaLabel=\"Previous track\"", shared, StringComparison.Ordinal);
        Assert.Contains("AriaLabel=\"Next track\"", shared, StringComparison.Ordinal);
        Assert.DoesNotContain("private RenderFragment SkipButton", bar, StringComparison.Ordinal);
        Assert.DoesNotContain("private RenderFragment SkipButton", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("private RenderFragment SkipButton", popup, StringComparison.Ordinal);
    }

    private static ListenQueueItem Item(string title) => new()
    {
        WorkId = Guid.NewGuid(),
        MediaType = "Music",
        Title = title,
    };

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MediaEngine.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find repository root.");
    }
}
