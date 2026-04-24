// app.js — Global JavaScript helpers for the Dashboard

/**
 * Ensures the dark theme class is applied to <body>.
 * Called from MainLayout on first render. Light mode has been removed.
 */
window.setThemeClass = function () {
    document.body.classList.add('app-dark');
};

/**
 * Registers a global Ctrl+K (or Cmd+K on Mac) keydown listener that invokes
 * the .NET OpenPalette() method on the provided DotNetObjectReference.
 *
 * Called once from MainLayout.OnAfterRenderAsync.
 *
 * @param {DotNetObjectReference} dotNetRef - Reference to the MainLayout component.
 */
window.registerCtrlK = function (dotNetRef) {
    // Guard: avoid double-registering on hot-reload.
    if (window._appCtrlKRegistered) return;
    window._appCtrlKRegistered = true;

    document.addEventListener('keydown', function (e) {
        if ((e.ctrlKey || e.metaKey) && e.key === 'k') {
            e.preventDefault();
            dotNetRef.invokeMethodAsync('OpenPalette');
        }
    });
};

// ── Device Context ─────────────────────────────────────────────────────────

/**
 * Detects the active device class for this browser session.
 * Priority:
 *   1. URL query parameter ?device=  (explicit override, highest priority)
 *   2. localStorage persisted device class
 *   3. Auto-detect: viewport ≤768px + touch → "mobile", else "web"
 *
 * Television and automotive must be explicitly selected (URL param or UI toggle).
 *
 * @returns {string} Device class: "web", "mobile", "television", or "automotive".
 */
window.detectDeviceClass = function () {
    // 1. URL parameter override
    var params = new URLSearchParams(window.location.search);
    var urlDevice = params.get('device');
    if (urlDevice && ['web', 'mobile', 'television', 'automotive'].indexOf(urlDevice.toLowerCase()) !== -1) {
        var normalised = urlDevice.toLowerCase();
        localStorage.setItem('device_class', normalised);
        return normalised;
    }

    // 2. Persisted in localStorage
    var stored = localStorage.getItem('device_class');
    if (stored && ['web', 'mobile', 'television', 'automotive'].indexOf(stored) !== -1) {
        return stored;
    }

    // 3. Auto-detect: mobile if narrow viewport + touch
    var isMobile = window.innerWidth <= 768 && ('ontouchstart' in window || navigator.maxTouchPoints > 0);
    var detected = isMobile ? 'mobile' : 'web';
    localStorage.setItem('device_class', detected);
    return detected;
};

/**
 * Persists the chosen device class to localStorage.
 * Called from the Dashboard when the user explicitly selects a device mode.
 *
 * @param {string} deviceClass - "web", "mobile", "television", or "automotive".
 */
window.setDeviceClass = function (deviceClass) {
    if (deviceClass && ['web', 'mobile', 'television', 'automotive'].indexOf(deviceClass.toLowerCase()) !== -1) {
        localStorage.setItem('device_class', deviceClass.toLowerCase());
    }
};

// -- Swimlane Scroll Arrows -----------------------------------------------

/**
 * Smoothly scrolls a swimlane element left or right by 75% of its visible width.
 * Called from PosterSwimlane.razor via JS interop.
 *
 * @param {HTMLElement} el  - The .swimlane-scroll container element.
 * @param {string} direction - "left" or "right".
 */
window.scrollSwimlane = function (el, direction) {
    if (!el) return;
    var scrollAmount = el.clientWidth * 0.75;
    el.scrollBy({
        left: direction === 'left' ? -scrollAmount : scrollAmount,
        behavior: 'smooth'
    });
};

/**
 * Returns the current scroll boundary state for a swimlane element.
 * @param {HTMLElement} el - The .swimlane-scroll container.
 * @returns {{ atStart: boolean, atEnd: boolean }}
 */
window.getSwimlaneScrollState = function (el) {
    if (!el) return { atStart: true, atEnd: true };
    return {
        atStart: el.scrollLeft <= 1,
        atEnd: el.scrollLeft + el.clientWidth >= el.scrollWidth - 1
    };
};

/**
 * Scrolls the swimlane and resolves with the boundary state after the
 * animation completes (~400 ms for smooth scroll).
 * @param {HTMLElement} el - The .swimlane-scroll container.
 * @param {string} direction - "left" or "right".
 * @returns {Promise<{ atStart: boolean, atEnd: boolean }>}
 */
window.scrollSwimlaneEx = function (el, direction) {
    if (!el) return Promise.resolve({ atStart: true, atEnd: true });
    var amount = el.clientWidth * 0.75;
    el.scrollBy({ left: direction === 'left' ? -amount : amount, behavior: 'smooth' });
    return new Promise(function (resolve) {
        setTimeout(function () {
            resolve({
                atStart: el.scrollLeft <= 1,
                atEnd: el.scrollLeft + el.clientWidth >= el.scrollWidth - 1
            });
        }, 420); // wait for smooth-scroll animation to settle
    });
};

// -- Discovery card hover positioning ------------------------------------

window.positionDiscoveryCardHover = function (cardEl) {
    if (!cardEl) return;

    window.requestAnimationFrame(function () {
        var panel = cardEl.querySelector('.discovery-card-hover-panel');
        if (!panel) return;

        var cardRect = cardEl.getBoundingClientRect();
        var panelWidth = panel.offsetWidth;
        var panelHeight = panel.offsetHeight;
        var gutter = 12;
        var computedStyle = window.getComputedStyle(cardEl);
        var hoverLift = parseFloat(computedStyle.getPropertyValue('--discovery-hover-lift')) || 0;
        var panelLeft = cardRect.left;
        var panelTop = cardRect.top - hoverLift;

        if (panelLeft < gutter) {
            panelLeft = gutter;
        }

        var overflowRight = panelLeft + panelWidth - (window.innerWidth - gutter);
        if (overflowRight > 0) {
            panelLeft -= overflowRight;
        }

        var overflowBottom = panelTop + panelHeight - (window.innerHeight - gutter);
        if (overflowBottom > 0) {
            panelTop -= overflowBottom;
        }

        var overflowTop = panelTop - gutter;
        if (overflowTop < 0) {
            panelTop += Math.abs(overflowTop);
        }

        cardEl.style.setProperty('--discovery-hover-panel-left', panelLeft + 'px');
        cardEl.style.setProperty('--discovery-hover-panel-top', panelTop + 'px');
    });
};

window.clearDiscoveryCardHover = function (cardEl) {
    if (!cardEl) return;
    cardEl.style.setProperty('--discovery-hover-shift-x', '0px');
    cardEl.style.setProperty('--discovery-hover-shift-y', '0px');
    cardEl.style.setProperty('--discovery-hover-panel-left', '-9999px');
    cardEl.style.setProperty('--discovery-hover-panel-top', '-9999px');
};

window.registerDiscoveryCardHover = function (cardEl) {
    if (!cardEl || cardEl.__discoveryHoverRegistered) return;

    var update = function () {
        window.positionDiscoveryCardHover(cardEl);
    };

    var clear = function () {
        window.clearDiscoveryCardHover(cardEl);
    };

    cardEl.addEventListener('mouseenter', update);
    cardEl.addEventListener('focusin', update);
    cardEl.addEventListener('mouseleave', clear);
    cardEl.addEventListener('focusout', clear);

    cardEl.__discoveryHoverUpdate = update;
    cardEl.__discoveryHoverClear = clear;
    cardEl.__discoveryHoverRegistered = true;
};

window.unregisterDiscoveryCardHover = function (cardEl) {
    if (!cardEl || !cardEl.__discoveryHoverRegistered) return;

    if (cardEl.__discoveryHoverUpdate) {
        cardEl.removeEventListener('mouseenter', cardEl.__discoveryHoverUpdate);
        cardEl.removeEventListener('focusin', cardEl.__discoveryHoverUpdate);
    }

    if (cardEl.__discoveryHoverClear) {
        cardEl.removeEventListener('mouseleave', cardEl.__discoveryHoverClear);
        cardEl.removeEventListener('focusout', cardEl.__discoveryHoverClear);
    }

    delete cardEl.__discoveryHoverUpdate;
    delete cardEl.__discoveryHoverClear;
    delete cardEl.__discoveryHoverRegistered;
};
// -- Alphabetical Grid scroll-to-letter ---------------------------------

/**
 * Smoothly scrolls the page to an element by its ID.
 * Used by AlphabeticalGrid.razor for the alphabet strip quick-jump.
 *
 * @param {string} elementId - The DOM ID of the target element (e.g. "az-A").
 */
window.scrollToLetter = function (elementId) {
    var el = document.getElementById(elementId);
    if (el) {
        el.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }
};

// -- Registry Card helpers ---------------------------------------------------

window.registryCardHelpers = {
    isNearBottomEdge: function (element, threshold) {
        if (!element) return false;
        var rect = element.getBoundingClientRect();
        return (window.innerHeight - rect.bottom) < threshold;
    }
};

// -- Registry Settings (localStorage) ----------------------------------------

window.registrySettings = {
    getCardSize: function () {
        return parseInt(localStorage.getItem('registry-card-size') || '80', 10);
    },
    setCardSize: function (size) {
        localStorage.setItem('registry-card-size', size.toString());
    }
};

window.listenPlayback = (function () {
    var stateKey = 'listen-playback-state';
    var commandKey = 'listen-playback-command';
    var popupName = 'tuvima-listen-mini-player';
    var channel = typeof BroadcastChannel !== 'undefined' ? new BroadcastChannel('tuvima-listen-playback') : null;
    var stateHandler = null;
    var commandHandler = null;
    var popupWindow = null;

    function notifyState(json) {
        if (stateHandler && json) {
            stateHandler.invokeMethodAsync('HandlePlaybackState', json);
        }
    }

    function notifyCommand(json) {
        if (commandHandler && json) {
            commandHandler.invokeMethodAsync('HandlePlaybackCommand', json);
        }
    }

    if (channel) {
        channel.onmessage = function (event) {
            if (!event || !event.data) return;

            if (event.data.type === 'state') {
                notifyState(event.data.json);
            }

            if (event.data.type === 'command') {
                notifyCommand(event.data.json);
            }
        };
    }

    window.addEventListener('storage', function (event) {
        if (!event) return;

        if (event.key === stateKey) {
            notifyState(event.newValue || '');
        }

        if (event.key === commandKey && event.newValue) {
            notifyCommand(event.newValue);
        }
    });

    return {
        getState: function () {
            return localStorage.getItem(stateKey);
        },
        setState: function (json) {
            localStorage.setItem(stateKey, json);
            if (channel) {
                channel.postMessage({ type: 'state', json: json });
            }
        },
        clearState: function () {
            localStorage.removeItem(stateKey);
            if (channel) {
                channel.postMessage({ type: 'state', json: '' });
            }
        },
        sendCommand: function (json) {
            if (!json) return;
            localStorage.setItem(commandKey, json);
            if (channel) {
                channel.postMessage({ type: 'command', json: json });
            }
        },
        registerStateHandler: function (dotNetRef) {
            stateHandler = dotNetRef;
        },
        registerCommandHandler: function (dotNetRef) {
            commandHandler = dotNetRef;
        },
        openPopup: function (url) {
            popupWindow = window.open(
                url,
                popupName,
                'popup=yes,width=380,height=700,resizable=yes,scrollbars=no'
            );

            if (popupWindow && typeof popupWindow.focus === 'function') {
                popupWindow.focus();
            }

            return !!popupWindow;
        },
        closePopup: function () {
            if (popupWindow && !popupWindow.closed) {
                popupWindow.close();
            }

            popupWindow = null;
        },
        registerPopupWindow: function () {
            window.addEventListener('beforeunload', function () {
                try {
                    var json = JSON.stringify({ action: 'popup-closed' });
                    localStorage.setItem(commandKey, json);
                    if (channel) {
                        channel.postMessage({ type: 'command', json: json });
                    }
                } catch {
                }
            });
        },
        closeOwnWindow: function () {
            window.close();
        },
        readAudioState: function (element) {
            if (!element) {
                return {
                    currentTime: 0,
                    duration: 0,
                    volume: 0.8,
                    muted: false,
                    paused: true
                };
            }

            return {
                currentTime: element.currentTime || 0,
                duration: isFinite(element.duration) ? element.duration : 0,
                volume: typeof element.volume === 'number' ? element.volume : 0.8,
                muted: !!element.muted,
                paused: !!element.paused
            };
        },
        playAudio: async function (element) {
            if (!element) return false;

            try {
                await element.play();
                return true;
            } catch {
                return false;
            }
        },
        pauseAudio: function (element) {
            if (!element) return;
            element.pause();
        },
        seekAudio: function (element, seconds) {
            if (!element) return;
            element.currentTime = Math.max(0, seconds || 0);
        },
        setVolume: function (element, volume) {
            if (!element) return;
            var next = Math.max(0, Math.min(1, volume || 0));
            element.volume = next;
        },
        setMuted: function (element, muted) {
            if (!element) return;
            element.muted = !!muted;
        }
    };
})();

window.listenUi = {
    getMode: function () {
        return localStorage.getItem('listen-ui-mode');
    },
    setMode: function (mode) {
        if (!mode) return;
        localStorage.setItem('listen-ui-mode', mode);
    },
    getMusicRoute: function () {
        return localStorage.getItem('listen-ui-music-route');
    },
    setMusicRoute: function (route) {
        if (!route) return;
        localStorage.setItem('listen-ui-music-route', route);
    },
    getSelectedArtist: function () {
        return localStorage.getItem('listen-ui-selected-artist');
    },
    setSelectedArtist: function (artistName) {
        if (!artistName) return;
        localStorage.setItem('listen-ui-selected-artist', artistName);
    },
    getTrackGridColumns: function (viewKey) {
        if (!viewKey) return null;
        return localStorage.getItem('listen-track-grid-columns-' + viewKey);
    },
    setTrackGridColumns: function (viewKey, json) {
        if (!viewKey || !json) return;
        localStorage.setItem('listen-track-grid-columns-' + viewKey, json);
    }
};
