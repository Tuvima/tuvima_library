const reconnectModal = document.getElementById("components-reconnect-modal");
reconnectModal.addEventListener("components-reconnect-state-changed", handleReconnectStateChanged);

const retryButton = document.getElementById("components-reconnect-button");
retryButton.addEventListener("click", retry);

const resumeButton = document.getElementById("components-resume-button");
resumeButton.addEventListener("click", resume);

const autoRetryBaseDelayMs = 1_000;
const autoRetryMaximumDelayMs = 10_000;
let autoRetryAttempt = 0;
let autoRetryTimer = null;
let retryInFlight = false;

function handleReconnectStateChanged(event) {
    if (event.detail.state === "show") {
        if (!reconnectModal.open) {
            reconnectModal.showModal();
        }
    } else if (event.detail.state === "hide") {
        resetAutomaticRetry();
        if (reconnectModal.open) {
            reconnectModal.close();
        }
    } else if (event.detail.state === "failed") {
        document.addEventListener("visibilitychange", retryWhenDocumentBecomesVisible);
        scheduleAutoRetry();
    } else if (event.detail.state === "rejected") {
        resetAutomaticRetry();
        location.reload();
    }
}

async function retry() {
    document.removeEventListener("visibilitychange", retryWhenDocumentBecomesVisible);
    clearAutoRetryTimer();

    if (retryInFlight) {
        return;
    }

    retryInFlight = true;
    let retryAfterFailure = false;
    try {
        // Reconnect will asynchronously return:
        // - true to mean success
        // - false to mean we reached the server, but it rejected the connection (e.g., unknown circuit ID)
        // - exception to mean we didn't reach the server (this can be sync or async)
        const successful = await Blazor.reconnect();
        if (successful) {
            resetAutomaticRetry();
            if (reconnectModal.open) {
                reconnectModal.close();
            }
        } else {
            // We have been able to reach the server, but the circuit is no longer available.
            // We'll reload the page so the user can continue using the app as quickly as possible.
            const resumeSuccessful = await Blazor.resumeCircuit();
            if (!resumeSuccessful) {
                location.reload();
            } else {
                resetAutomaticRetry();
                if (reconnectModal.open) {
                    reconnectModal.close();
                }
            }
        }
    } catch (err) {
        // We got an exception, server is currently unavailable
        document.addEventListener("visibilitychange", retryWhenDocumentBecomesVisible);
        retryAfterFailure = true;
    } finally {
        retryInFlight = false;
        if (retryAfterFailure) {
            scheduleAutoRetry();
        }
    }
}

function scheduleAutoRetry() {
    if (autoRetryTimer !== null) {
        return;
    }

    const delay = Math.min(
        autoRetryBaseDelayMs * (2 ** autoRetryAttempt),
        autoRetryMaximumDelayMs);
    autoRetryAttempt += 1;
    autoRetryTimer = window.setTimeout(() => {
        autoRetryTimer = null;
        void retry();
    }, delay);
}

function clearAutoRetryTimer() {
    if (autoRetryTimer !== null) {
        window.clearTimeout(autoRetryTimer);
        autoRetryTimer = null;
    }
}

function resetAutomaticRetry() {
    clearAutoRetryTimer();
    autoRetryAttempt = 0;
    document.removeEventListener("visibilitychange", retryWhenDocumentBecomesVisible);
}

async function resume() {
    try {
        const successful = await Blazor.resumeCircuit();
        if (!successful) {
            location.reload();
        }
    } catch (err) {
        console.debug("Circuit resume failed.", err);
        reconnectModal.classList.replace("components-reconnect-paused", "components-reconnect-resume-failed");
    }
}

async function retryWhenDocumentBecomesVisible() {
    if (document.visibilityState === "visible") {
        autoRetryAttempt = 0;
        await retry();
    }
}
