using MediaEngine.Contracts.Playback;
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
            PlaybackRate = 1.5d,
            Experience = PlayerExperienceModes.Audiobook,
            NeedsUserGestureToStart = true,
            IsPlaying = false,
            IsPopupOpen = true,
            AudiobookHistory =
            [
                new AudiobookListenHistoryItemDto
                {
                    Id = Guid.NewGuid(),
                    WorkId = Guid.NewGuid(),
                    AssetId = Guid.NewGuid(),
                    Title = "Previous audiobook position",
                    PositionSeconds = 1240,
                    ProgressPct = 27.5,
                    StartedAt = DateTimeOffset.UtcNow.AddMinutes(-3),
                    EndedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
                },
            ],
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
        Assert.Equal(1.5d, roundTrip.PlaybackRate);
        Assert.Equal(PlayerExperienceModes.Audiobook, roundTrip.Experience);
        Assert.True(roundTrip.NeedsUserGestureToStart);
        Assert.False(roundTrip.IsPlaying);
        Assert.True(roundTrip.IsPopupOpen);
        Assert.Single(roundTrip.AudiobookHistory);
    }

    [Fact]
    public async Task PlayAudiobookAsync_UsesSingleItemModeAndAppliesResumeRewind()
    {
        var service = new ListenPlaybackService(null!, null!);
        var audiobook = CreateAudiobookItem("Dungeon Crawler Carl", "stream://dungeon-crawler-carl") with
        {
            InitialPositionSeconds = 123,
        };

        await service.PlayAudiobookAsync(audiobook, "Dungeon Crawler Carl");

        Assert.True(service.IsAudiobookMode);
        Assert.Single(service.Queue);
        Assert.Equal("Dungeon Crawler Carl", service.CurrentItem?.Title);
        Assert.Equal(113, service.CurrentTimeSeconds);
        Assert.Equal(1.25d, service.PlaybackRate);
        Assert.True(service.IsPlaying);
        Assert.False(service.NeedsUserGestureToStart);
    }

    [Fact]
    public async Task AddQueueItemAsync_Audiobook_ReplacesMusicQueueInsteadOfAppending()
    {
        var service = new ListenPlaybackService(null!, null!);
        await service.AddQueueItemAsync(CreateQueueItem("Current Song", "stream://song"));

        await service.AddQueueItemAsync(CreateAudiobookItem("Dungeon Crawler Carl", "stream://book"));

        Assert.True(service.IsAudiobookMode);
        Assert.Single(service.Queue);
        Assert.Equal("Dungeon Crawler Carl", service.Queue[0].Title);
        Assert.Single(service.History);
        Assert.Equal("Current Song", service.History[0].Title);
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

    private static ListenQueueItem CreateAudiobookItem(string title, string streamUrl) => new()
    {
        WorkId = Guid.NewGuid(),
        AssetId = Guid.NewGuid(),
        MediaType = "Audiobooks",
        Title = title,
        Subtitle = "Matt Dinniman",
        Album = title,
        Duration = "11:32:00",
        StreamUrl = streamUrl,
    };
}
