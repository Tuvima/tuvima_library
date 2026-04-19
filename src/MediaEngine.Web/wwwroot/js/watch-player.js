const playerState = new WeakMap();

function buildState(video, dotNetRef) {
    let lastSentAt = 0;

    const sendProgress = (ended = false) => {
        if (!video || !dotNetRef) {
            return;
        }

        const duration = video.duration || 0;
        if (!duration) {
            return;
        }

        const now = Date.now();
        if (!ended && now - lastSentAt < 10000) {
            return;
        }

        lastSentAt = now;
        dotNetRef.invokeMethodAsync("OnPlayerProgress", video.currentTime || 0, duration, ended);
    };

    const onTimeUpdate = () => sendProgress(false);
    const onPause = () => sendProgress(false);
    const onEnded = () => sendProgress(true);
    const onVisibilityChange = () => {
        if (document.visibilityState === "hidden") {
            sendProgress(false);
        }
    };
    const onBeforeUnload = () => sendProgress(false);

    return {
        onTimeUpdate,
        onPause,
        onEnded,
        onVisibilityChange,
        onBeforeUnload
    };
}

export function attachPlayer(video, dotNetRef) {
    if (!video || playerState.has(video)) {
        return;
    }

    const state = buildState(video, dotNetRef);
    playerState.set(video, state);

    video.addEventListener("timeupdate", state.onTimeUpdate);
    video.addEventListener("pause", state.onPause);
    video.addEventListener("ended", state.onEnded);
    document.addEventListener("visibilitychange", state.onVisibilityChange);
    window.addEventListener("beforeunload", state.onBeforeUnload);
}

export function restoreProgress(video, progressPct) {
    if (!video || !progressPct || progressPct <= 0 || progressPct >= 99.5) {
        return;
    }

    const seek = () => {
        if (!video.duration || video.duration <= 0) {
            return;
        }

        video.currentTime = Math.max(0, (progressPct / 100) * video.duration);
    };

    if (video.readyState >= 1) {
        seek();
        return;
    }

    video.addEventListener("loadedmetadata", seek, { once: true });
}

export function detachPlayer(video) {
    if (!video) {
        return;
    }

    const state = playerState.get(video);
    if (!state) {
        return;
    }

    video.removeEventListener("timeupdate", state.onTimeUpdate);
    video.removeEventListener("pause", state.onPause);
    video.removeEventListener("ended", state.onEnded);
    document.removeEventListener("visibilitychange", state.onVisibilityChange);
    window.removeEventListener("beforeunload", state.onBeforeUnload);
    playerState.delete(video);
}
