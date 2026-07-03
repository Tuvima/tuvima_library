using MediaEngine.Web.Services.Playback;
using MudBlazor;

namespace MediaEngine.Web.Components.Shared;

public enum PlaybackControlKey
{
    PlayPause,
    Timeline,
    PreviousItem,
    NextItem,
    SkipBack,
    SkipForward,
    Volume,
    Mute,
    Queue,
    History,
    Lyrics,
    Chapters,
    Bookmarks,
    SleepTimer,
    Speed,
    Cast,
    More,
    Captions,
    AudioTrack,
    Quality,
    Fullscreen,
    PictureInPicture,
    Shuffle,
    Repeat,
    Favorite,
    Close,
    Expand,
    Resume,
    SkipIntro,
    SkipCredits,
}

public enum PlaybackControlSurface
{
    /// <summary>Persistent minimized player surface for music, audiobooks, and future video miniplayers.</summary>
    Bottom,
    SidePanel,
    Popup,
    Fullscreen,
}

public enum PlaybackControlPlacement
{
    Transport,
    Timeline,
    Utility,
    ToolStrip,
}

public sealed record PlaybackControlState(
    string? ActiveSheet = null,
    double PlaybackRate = 1d,
    bool IsPlaying = false,
    bool IsMuted = false,
    bool HasChapters = false,
    bool HasQueue = true,
    bool HasLyrics = false,
    bool IsSleepTimerActive = false,
    string? SleepTimerValueText = null,
    int HistoryCount = 0,
    int BookmarkCount = 0);

public sealed record PlaybackControlDefinition(
    PlaybackControlKey Key,
    string Label,
    string AriaLabel,
    string Icon,
    PlaybackControlPlacement Placement,
    string Command,
    string? Sheet = null,
    string? ValueText = null,
    string? BadgeText = null,
    bool IsActive = false,
    bool IsDisabled = false);

public static class PlaybackControlCatalog
{
    public static IReadOnlyList<PlaybackControlDefinition> Build(
        PlaybackExperience experience,
        PlaybackControlSurface surface,
        PlaybackControlState state)
    {
        var controls = new List<PlaybackControlDefinition>
        {
            new(PlaybackControlKey.PlayPause, state.IsPlaying ? "Pause" : "Play", "Play or pause", state.IsPlaying ? Icons.Material.Filled.Pause : Icons.Material.Filled.PlayArrow, PlaybackControlPlacement.Transport, "toggle-play"),
            new(PlaybackControlKey.Timeline, "Playback position", "Playback position", Icons.Material.Outlined.Timeline, PlaybackControlPlacement.Timeline, "seek"),
            PreviousNext(experience, isNext: false),
            Next(experience),
        };

        if (experience is PlaybackExperience.Audiobook or PlaybackExperience.Video)
        {
            controls.Add(new(PlaybackControlKey.SkipBack, "Back", "Skip back", Icons.Material.Outlined.Replay10, PlaybackControlPlacement.Transport, "skip-back"));
            controls.Add(new(PlaybackControlKey.SkipForward, "Forward", "Skip forward", Icons.Material.Outlined.Forward10, PlaybackControlPlacement.Transport, "skip-forward"));
        }

        controls.Add(new(PlaybackControlKey.Mute, state.IsMuted ? "Unmute" : "Mute", "Mute or unmute", state.IsMuted ? Icons.Material.Outlined.VolumeOff : Icons.Material.Outlined.VolumeUp, PlaybackControlPlacement.Utility, "toggle-mute", IsActive: state.IsMuted));
        controls.Add(new(PlaybackControlKey.Volume, "Volume", "Volume", Icons.Material.Outlined.VolumeUp, PlaybackControlPlacement.Utility, "set-volume"));
        controls.Add(new(PlaybackControlKey.Cast, "Cast", "Playback device", Icons.Material.Outlined.Cast, PlaybackControlPlacement.Utility, "cast"));
        controls.Add(new(PlaybackControlKey.More, "More", "More options", Icons.Material.Outlined.MoreVert, PlaybackControlPlacement.Utility, "more"));

        AddExperienceControls(controls, experience, surface, state);
        return controls;
    }

    public static IReadOnlyList<PlaybackControlDefinition> BuildToolStrip(
        PlaybackExperience experience,
        PlaybackControlSurface surface,
        PlaybackControlState state)
    {
        return Build(experience, surface, state)
            .Where(control => control.Placement == PlaybackControlPlacement.ToolStrip)
            .ToList();
    }

    private static PlaybackControlDefinition PreviousNext(PlaybackExperience experience, bool isNext)
    {
        var label = experience switch
        {
            PlaybackExperience.Audiobook => isNext ? "Next chapter" : "Previous chapter",
            PlaybackExperience.Video => isNext ? "Next" : "Previous",
            _ => isNext ? "Next track" : "Previous track",
        };

        var key = isNext ? PlaybackControlKey.NextItem : PlaybackControlKey.PreviousItem;
        var icon = isNext ? Icons.Material.Filled.SkipNext : Icons.Material.Filled.SkipPrevious;
        var command = isNext
            ? experience == PlaybackExperience.Audiobook ? "play-next-chapter" : "play-next"
            : experience == PlaybackExperience.Audiobook ? "play-previous-chapter" : "play-previous";

        return new(key, label, label, icon, PlaybackControlPlacement.Transport, command);
    }

    private static PlaybackControlDefinition Next(PlaybackExperience experience) => PreviousNext(experience, isNext: true);

    private static void AddExperienceControls(
        ICollection<PlaybackControlDefinition> controls,
        PlaybackExperience experience,
        PlaybackControlSurface surface,
        PlaybackControlState state)
    {
        switch (experience)
        {
            case PlaybackExperience.Music:
                controls.Add(new(PlaybackControlKey.Queue, "Queue", "Queue", Icons.Material.Outlined.QueueMusic, PlaybackControlPlacement.ToolStrip, "queue", IsDisabled: !state.HasQueue));
                controls.Add(new(PlaybackControlKey.History, "History", "History", Icons.Material.Outlined.History, PlaybackControlPlacement.ToolStrip, "history"));
                controls.Add(new(PlaybackControlKey.Lyrics, "Lyrics", "Lyrics", Icons.Material.Outlined.Lyrics, PlaybackControlPlacement.ToolStrip, "lyrics", IsDisabled: !state.HasLyrics));
                controls.Add(new(PlaybackControlKey.Shuffle, "Shuffle", "Shuffle", Icons.Material.Outlined.Shuffle, PlaybackControlPlacement.ToolStrip, "shuffle"));
                controls.Add(new(PlaybackControlKey.Repeat, "Repeat", "Repeat", Icons.Material.Outlined.Repeat, PlaybackControlPlacement.ToolStrip, "repeat"));
                break;
            case PlaybackExperience.Audiobook:
                controls.Add(Tool(PlaybackControlKey.Speed, "Speed", Icons.Material.Outlined.Speed, "speed", state, ValueText: FormatSpeed(state.PlaybackRate)));
                controls.Add(Tool(PlaybackControlKey.Chapters, "Chapters", Icons.Material.Outlined.FormatListBulleted, "chapters", state, IsDisabled: !state.HasChapters));
                controls.Add(Tool(PlaybackControlKey.History, "History", Icons.Material.Outlined.History, "history", state));
                controls.Add(Tool(PlaybackControlKey.Bookmarks, "Bookmark", Icons.Material.Outlined.BookmarkBorder, "bookmarks", state));
                controls.Add(Tool(PlaybackControlKey.SleepTimer, "Sleep", Icons.Material.Outlined.Timer, "sleep", state, BadgeText: state.SleepTimerValueText, IsActive: state.IsSleepTimerActive));
                break;
            case PlaybackExperience.Video:
                controls.Add(Tool(PlaybackControlKey.Speed, "Speed", Icons.Material.Outlined.Speed, "speed", state, ValueText: FormatSpeed(state.PlaybackRate)));
                controls.Add(Tool(PlaybackControlKey.Chapters, "Chapters", Icons.Material.Outlined.FormatListBulleted, "chapters", state, IsDisabled: !state.HasChapters));
                controls.Add(new(PlaybackControlKey.Queue, "Queue", "Queue", Icons.Material.Outlined.QueuePlayNext, PlaybackControlPlacement.ToolStrip, "queue", IsDisabled: !state.HasQueue));
                controls.Add(new(PlaybackControlKey.History, "History", "History", Icons.Material.Outlined.History, PlaybackControlPlacement.ToolStrip, "history"));
                controls.Add(new(PlaybackControlKey.Captions, "Captions", "Captions and subtitles", Icons.Material.Outlined.ClosedCaption, PlaybackControlPlacement.ToolStrip, "captions"));
                controls.Add(new(PlaybackControlKey.AudioTrack, "Audio", "Audio track", Icons.Material.Outlined.RecordVoiceOver, PlaybackControlPlacement.ToolStrip, "audio-track"));
                controls.Add(new(PlaybackControlKey.Quality, "Quality", "Playback quality", Icons.Material.Outlined.HighQuality, PlaybackControlPlacement.ToolStrip, "quality"));
                controls.Add(new(PlaybackControlKey.Fullscreen, "Fullscreen", "Fullscreen", Icons.Material.Outlined.Fullscreen, PlaybackControlPlacement.Utility, "fullscreen"));
                controls.Add(new(PlaybackControlKey.PictureInPicture, "PiP", "Picture in picture", Icons.Material.Outlined.PictureInPictureAlt, PlaybackControlPlacement.Utility, "picture-in-picture"));
                controls.Add(new(PlaybackControlKey.SkipIntro, "Intro", "Skip intro", Icons.Material.Outlined.FastForward, PlaybackControlPlacement.Utility, "skip-intro", IsDisabled: true));
                controls.Add(new(PlaybackControlKey.SkipCredits, "Credits", "Skip credits", Icons.Material.Outlined.FastForward, PlaybackControlPlacement.Utility, "skip-credits", IsDisabled: true));
                if (surface == PlaybackControlSurface.Bottom)
                {
                    controls.Add(new(PlaybackControlKey.Expand, "Expand", "Expand player", Icons.Material.Outlined.OpenInFull, PlaybackControlPlacement.Utility, "expand-player"));
                    controls.Add(new(PlaybackControlKey.Resume, "Resume", "Resume video", Icons.Material.Outlined.PlayCircle, PlaybackControlPlacement.Utility, "resume-video"));
                    controls.Add(new(PlaybackControlKey.Close, "Close", "Close mini player", Icons.Material.Outlined.Close, PlaybackControlPlacement.Utility, "close-player"));
                }
                break;
        }
    }

    private static PlaybackControlDefinition Tool(
        PlaybackControlKey key,
        string label,
        string icon,
        string sheet,
        PlaybackControlState state,
        string? ValueText = null,
        string? BadgeText = null,
        bool IsActive = false,
        bool IsDisabled = false)
    {
        return new(
            key,
            label,
            ValueText is null ? label : $"{label} {ValueText}",
            icon,
            PlaybackControlPlacement.ToolStrip,
            $"open-{sheet}",
            sheet,
            ValueText,
            BadgeText,
            IsActive || string.Equals(state.ActiveSheet, sheet, StringComparison.Ordinal),
            IsDisabled);
    }

    private static string FormatSpeed(double rate) => $"{Math.Clamp(rate, 0.5d, 3d):0.#}x";
}
