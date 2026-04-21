using MediaEngine.Web.Services.Playback;

namespace MediaEngine.Web.Tests;

public sealed class ListenPlaybackServiceTests
{
    [Fact]
    public void CreateSnapshot_RoundTripsQueueHistoryAndTransportState()
    {
        var service = new ListenPlaybackService(null!, null!);
        var snapshot = new ListenPlaybackSnapshot
        {
            Queue =
            [
                CreateQueueItem("Current Song", "stream://current"),
                CreateQueueItem("Next Song", "stream://next"),
            ],
            History =
            [
                CreateQueueItem("Previous Song", "stream://previous") with { PlayedAt = new DateTimeOffset(2026, 4, 21, 12, 0, 0, TimeSpan.Zero) }
            ],
            CurrentIndex = 0,
            SourceLabel = "All Music",
            IsPanelOpen = true,
            ActiveTab = ListenPlaybackTabs.History,
            CurrentTimeSeconds = 42,
            DurationSeconds = 180,
            Volume = 0.55,
            IsMuted = true,
            IsPlaying = false,
            IsPopupOpen = true,
        };

        service.RestoreState(snapshot);

        var roundTrip = service.CreateSnapshot();

        Assert.Equal(2, roundTrip.Queue.Count);
        Assert.Single(roundTrip.History);
        Assert.Equal(ListenPlaybackTabs.History, roundTrip.ActiveTab);
        Assert.Equal(42, roundTrip.CurrentTimeSeconds);
        Assert.Equal(180, roundTrip.DurationSeconds);
        Assert.Equal(0.55, roundTrip.Volume, 3);
        Assert.True(roundTrip.IsMuted);
        Assert.False(roundTrip.IsPlaying);
        Assert.True(roundTrip.IsPopupOpen);
    }

    [Fact]
    public void ClearUpcoming_RemovesOnlyFutureQueueItems()
    {
        var service = new ListenPlaybackService(null!, null!);
        service.RestoreState(new ListenPlaybackSnapshot
        {
            Queue =
            [
                CreateQueueItem("Current", "stream://current"),
                CreateQueueItem("Upcoming One", "stream://one"),
                CreateQueueItem("Upcoming Two", "stream://two"),
            ],
            CurrentIndex = 0,
        });

        service.ClearUpcoming();

        Assert.Single(service.Queue);
        Assert.Equal("Current", service.Queue[0].Title);
    }

    [Fact]
    public void RemoveUpcomingAt_DoesNotRemoveCurrentItem()
    {
        var service = new ListenPlaybackService(null!, null!);
        service.RestoreState(new ListenPlaybackSnapshot
        {
            Queue =
            [
                CreateQueueItem("Current", "stream://current"),
                CreateQueueItem("Upcoming", "stream://upcoming"),
            ],
            CurrentIndex = 0,
        });

        service.RemoveUpcomingAt(0);

        Assert.Equal(2, service.Queue.Count);

        service.RemoveUpcomingAt(1);

        Assert.Single(service.Queue);
        Assert.Equal("Current", service.Queue[0].Title);
    }

    [Fact]
    public void ClosePlayer_ClearsQueueAndHistory()
    {
        var service = new ListenPlaybackService(null!, null!);
        service.RestoreState(new ListenPlaybackSnapshot
        {
            Queue = [CreateQueueItem("Current", "stream://current")],
            History = [CreateQueueItem("Previous", "stream://previous")],
            CurrentIndex = 0,
            IsPlaying = true,
            IsPopupOpen = true,
        });

        service.ClosePlayer();

        Assert.Empty(service.Queue);
        Assert.Empty(service.History);
        Assert.False(service.HasQueue);
        Assert.False(service.IsPopupOpen);
        Assert.True(service.IsDismissed);
    }

    private static ListenQueueItem CreateQueueItem(string title, string streamUrl) => new()
    {
        WorkId = Guid.NewGuid(),
        MediaType = "Music",
        Title = title,
        Subtitle = "Artist",
        Album = "Album",
        Duration = "3:30",
        StreamUrl = streamUrl,
    };
}
