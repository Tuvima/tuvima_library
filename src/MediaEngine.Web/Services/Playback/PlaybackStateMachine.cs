namespace MediaEngine.Web.Services.Playback;

public sealed class PlaybackStateMachine
{
    public PlaybackPhase Phase { get; private set; } = PlaybackPhase.Idle;

    public PlaybackPhase Transition(PlaybackCommandKind command)
    {
        Phase = command switch
        {
            PlaybackCommandKind.ClosePlayer => PlaybackPhase.Idle,
            PlaybackCommandKind.MarkNeedsUserGestureToStart => PlaybackPhase.NeedsGesture,
            PlaybackCommandKind.Pause => PlaybackPhase.Paused,
            PlaybackCommandKind.TogglePlay when Phase == PlaybackPhase.Playing => PlaybackPhase.Paused,
            PlaybackCommandKind.TogglePlay => PlaybackPhase.Playing,
            PlaybackCommandKind.PlayNext or PlaybackCommandKind.PlayPrevious or PlaybackCommandKind.PlayIndex or PlaybackCommandKind.PlayQueueItem => PlaybackPhase.Loading,
            PlaybackCommandKind.UpdateTransportState when Phase == PlaybackPhase.Loading => PlaybackPhase.Ready,
            PlaybackCommandKind.UpdateTransportState => Phase,
            _ => Phase,
        };

        return Phase;
    }

    public void SetTransportState(bool? isPlaying, bool? needsUserGesture, string? error)
    {
        if (!string.IsNullOrWhiteSpace(error))
        {
            Phase = PlaybackPhase.Error;
            return;
        }

        if (needsUserGesture == true)
        {
            Phase = PlaybackPhase.NeedsGesture;
            return;
        }

        if (isPlaying.HasValue)
        {
            Phase = isPlaying.Value ? PlaybackPhase.Playing : PlaybackPhase.Paused;
        }
    }

    public void SetEnded() => Phase = PlaybackPhase.Ended;
    public void SetIdle() => Phase = PlaybackPhase.Idle;
    public void SetLoading() => Phase = PlaybackPhase.Loading;
}

