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
    window.unregisterCtrlK();

    window._appCtrlKHandler = function (e) {
        if ((e.ctrlKey || e.metaKey) && e.key === 'k') {
            e.preventDefault();
            dotNetRef.invokeMethodAsync('OpenPalette');
        }
    };

    document.addEventListener('keydown', window._appCtrlKHandler);
};

window.unregisterCtrlK = function () {
    if (!window._appCtrlKHandler) {
        return;
    }

    document.removeEventListener('keydown', window._appCtrlKHandler);
    window._appCtrlKHandler = null;
};

// -- Device Context ---------------------------------------------------------

/**
 * Detects the active device class for this browser session.
 * Priority:
 *   1. URL query parameter ?device=  (explicit override, highest priority)
 *   2. localStorage persisted device class
 *   3. Auto-detect: viewport =768px + touch ? "mobile", else "web"
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

// -- Media tile hover positioning ------------------------------------

window.getMediaTileHoverHost = function () {
    return document.getElementById('media-tile-hover-host') || document.body;
};

window.positionMediaTileHover = function (cardEl) {
    if (!cardEl) return;

    if (cardEl.__mediaTileHoverFrame) {
        cardEl.__mediaTileHoverNeedsReposition = true;
        return;
    }

    cardEl.__mediaTileHoverFrame = window.requestAnimationFrame(function () {
        cardEl.__mediaTileHoverFrame = null;
        var panel = cardEl.__mediaTileHoverPanel || cardEl.querySelector('.media-tile-hover-panel');
        if (!panel) return;

        var cardRect = cardEl.getBoundingClientRect();
        var gutter = 12;
        var computedStyle = window.getComputedStyle(cardEl);
        var hoverLift = parseFloat(computedStyle.getPropertyValue('--media-tile-hover-lift')) || 0;
        var prefersBottomPlacement = panel.classList.contains('is-placement-bottom')
            && window.innerWidth > 700;
        panel.style.width = '';

        var panelWidth = panel.offsetWidth;
        var panelHeight = panel.offsetHeight;
        var isSideBySidePortrait = !prefersBottomPlacement
            && panel.classList.contains('is-art-popover')
            && panel.classList.contains('is-portrait')
            && window.innerWidth > 700;
        var panelLeft = cardRect.left;
        var panelTop = cardRect.top - hoverLift;

        if (prefersBottomPlacement) {
            panelLeft = cardRect.left + (cardRect.width / 2) - (panelWidth / 2);
            panelTop = cardRect.top - hoverLift;
        } else if (isSideBySidePortrait) {
            var rightCandidate = cardRect.right + gutter;
            var leftCandidate = cardRect.left - panelWidth - gutter;

            if (rightCandidate + panelWidth <= window.innerWidth - gutter) {
                panelLeft = rightCandidate;
            } else if (leftCandidate >= gutter) {
                panelLeft = leftCandidate;
            }
        }

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

        panel.style.left = panelLeft + 'px';
        panel.style.top = panelTop + 'px';

        if (cardEl.__mediaTileHoverNeedsReposition) {
            cardEl.__mediaTileHoverNeedsReposition = false;
            window.positionMediaTileHover(cardEl);
        }
    });
};

window.mountMediaTileHover = function (cardEl) {
    if (!cardEl) return null;

    var panel = cardEl.querySelector('.media-tile-hover-panel');
    if (!panel) return null;

    if (!panel.__mediaTileHoverOriginalParent) {
        panel.__mediaTileHoverOriginalParent = panel.parentElement;
    }

    if (!panel.__mediaTileHoverOriginalNextSibling) {
        panel.__mediaTileHoverOriginalNextSibling = panel.nextSibling;
    }

    var host = window.getMediaTileHoverHost();
    if (panel.parentElement !== host) {
        host.appendChild(panel);
    }

    return panel;
};

window.restoreMediaTileHover = function (cardEl) {
    if (!cardEl) return;

    var panel = cardEl.querySelector('.media-tile-hover-panel') || cardEl.__mediaTileHoverPanel;
    if (!panel || !panel.__mediaTileHoverOriginalParent) return;

    var originalParent = panel.__mediaTileHoverOriginalParent;
    var originalNextSibling = panel.__mediaTileHoverOriginalNextSibling;

    if (originalNextSibling && originalNextSibling.parentNode === originalParent) {
        originalParent.insertBefore(panel, originalNextSibling);
    } else {
        originalParent.appendChild(panel);
    }
};

window.showMediaTileHover = function (cardEl) {
    if (!cardEl) return;

    var panel = window.mountMediaTileHover(cardEl);
    if (!panel) return;

    cardEl.__mediaTileHoverPanel = panel;

    if (cardEl.__mediaTileHideTimer) {
        window.clearTimeout(cardEl.__mediaTileHideTimer);
        cardEl.__mediaTileHideTimer = null;
    }

    cardEl.classList.add('is-hover-active');
    panel.classList.add('is-visible');
    window.positionMediaTileHover(cardEl);
};

window.clearMediaTileHover = function (cardEl) {
    if (!cardEl) return;

    var panel = cardEl.__mediaTileHoverPanel || cardEl.querySelector('.media-tile-hover-panel');
    cardEl.classList.remove('is-hover-active');

    if (cardEl.__mediaTileShowTimer) {
        window.clearTimeout(cardEl.__mediaTileShowTimer);
        cardEl.__mediaTileShowTimer = null;
    }

    if (!panel) return;

    panel.classList.remove('is-visible');
    panel.style.left = '-9999px';
    panel.style.top = '-9999px';

    if (!cardEl.__mediaTilePinned) {
        window.setTimeout(function () {
            if (!panel.classList.contains('is-visible') && panel.parentElement === window.getMediaTileHoverHost()) {
                window.restoreMediaTileHover(cardEl);
            }
        }, 180);
    }
};

window.setMediaTileHoverExpanded = function (cardEl, expanded) {
    if (!cardEl) return;

    cardEl.__mediaTilePinned = !!expanded;

    if (expanded) {
        window.showMediaTileHover(cardEl);
        return;
    }

    window.clearMediaTileHover(cardEl);
};

window.registerMediaTileHover = function (cardEl) {
    if (!cardEl || cardEl.__mediaTileHoverRegistered) return;

    var show = function () {
        if (cardEl.__mediaTileHideTimer) {
            window.clearTimeout(cardEl.__mediaTileHideTimer);
            cardEl.__mediaTileHideTimer = null;
        }

        if (cardEl.__mediaTileShowTimer) {
            window.clearTimeout(cardEl.__mediaTileShowTimer);
        }

        cardEl.__mediaTileShowTimer = window.setTimeout(function () {
            cardEl.__mediaTileShowTimer = null;
            window.showMediaTileHover(cardEl);
        }, 260);
    };

    var scheduleHide = function () {
        if (cardEl.__mediaTilePinned) return;
        if (cardEl.__mediaTileShowTimer) {
            window.clearTimeout(cardEl.__mediaTileShowTimer);
            cardEl.__mediaTileShowTimer = null;
        }

        if (cardEl.__mediaTileHideTimer) {
            window.clearTimeout(cardEl.__mediaTileHideTimer);
        }

        cardEl.__mediaTileHideTimer = window.setTimeout(function () {
            window.clearMediaTileHover(cardEl);
        }, 80);
    };

    var keepOpen = function () {
        if (cardEl.__mediaTileShowTimer) {
            window.clearTimeout(cardEl.__mediaTileShowTimer);
            cardEl.__mediaTileShowTimer = null;
        }

        if (cardEl.__mediaTileHideTimer) {
            window.clearTimeout(cardEl.__mediaTileHideTimer);
            cardEl.__mediaTileHideTimer = null;
        }
    };

    cardEl.addEventListener('mouseenter', show);
    cardEl.addEventListener('focusin', show);
    cardEl.addEventListener('mouseleave', scheduleHide);
    cardEl.addEventListener('focusout', scheduleHide);

    var panel = cardEl.querySelector('.media-tile-hover-panel');
    if (panel) {
        panel.addEventListener('mouseenter', keepOpen);
        panel.addEventListener('focusin', keepOpen);
        panel.addEventListener('mouseleave', scheduleHide);
        panel.addEventListener('focusout', scheduleHide);
    }

    var reposition = function () {
        var activePanel = cardEl.__mediaTileHoverPanel || panel;
        if (activePanel && activePanel.classList.contains('is-visible')) {
            window.positionMediaTileHover(cardEl);
        }
    };

    window.addEventListener('resize', reposition);
    window.addEventListener('scroll', reposition, true);

    cardEl.__mediaTileHoverShow = show;
    cardEl.__mediaTileHoverHide = scheduleHide;
    cardEl.__mediaTileHoverKeepOpen = keepOpen;
    cardEl.__mediaTileHoverReposition = reposition;
    cardEl.__mediaTileHoverRegistered = true;
};

window.unregisterMediaTileHover = function (cardEl) {
    if (!cardEl || !cardEl.__mediaTileHoverRegistered) return;

    var panel = cardEl.__mediaTileHoverPanel || cardEl.querySelector('.media-tile-hover-panel');

    if (cardEl.__mediaTileHoverShow) {
        cardEl.removeEventListener('mouseenter', cardEl.__mediaTileHoverShow);
        cardEl.removeEventListener('focusin', cardEl.__mediaTileHoverShow);
    }

    if (cardEl.__mediaTileHoverHide) {
        cardEl.removeEventListener('mouseleave', cardEl.__mediaTileHoverHide);
        cardEl.removeEventListener('focusout', cardEl.__mediaTileHoverHide);
    }

    if (panel && cardEl.__mediaTileHoverKeepOpen) {
        panel.removeEventListener('mouseenter', cardEl.__mediaTileHoverKeepOpen);
        panel.removeEventListener('focusin', cardEl.__mediaTileHoverKeepOpen);
        panel.removeEventListener('mouseleave', cardEl.__mediaTileHoverHide);
        panel.removeEventListener('focusout', cardEl.__mediaTileHoverHide);
    }

    if (cardEl.__mediaTileHoverReposition) {
        window.removeEventListener('resize', cardEl.__mediaTileHoverReposition);
        window.removeEventListener('scroll', cardEl.__mediaTileHoverReposition, true);
    }

    if (cardEl.__mediaTileHideTimer) {
        window.clearTimeout(cardEl.__mediaTileHideTimer);
    }
    if (cardEl.__mediaTileShowTimer) {
        window.clearTimeout(cardEl.__mediaTileShowTimer);
    }
    if (cardEl.__mediaTileHoverFrame) {
        window.cancelAnimationFrame(cardEl.__mediaTileHoverFrame);
    }

    cardEl.__mediaTilePinned = false;
    window.clearMediaTileHover(cardEl);
    window.restoreMediaTileHover(cardEl);

    delete cardEl.__mediaTileHoverPanel;
    delete cardEl.__mediaTileHoverShow;
    delete cardEl.__mediaTileHoverHide;
    delete cardEl.__mediaTileHoverKeepOpen;
    delete cardEl.__mediaTileHoverReposition;
    delete cardEl.__mediaTileHoverFrame;
    delete cardEl.__mediaTileHoverNeedsReposition;
    delete cardEl.__mediaTileShowTimer;
    delete cardEl.__mediaTileHideTimer;
    delete cardEl.__mediaTilePinned;
    delete cardEl.__mediaTileHoverRegistered;
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

// -- LibraryItem Card helpers ---------------------------------------------------

window.libraryItemCardHelpers = {
    isNearBottomEdge: function (element, threshold) {
        if (!element) return false;
        var rect = element.getBoundingClientRect();
        return (window.innerHeight - rect.bottom) < threshold;
    }
};

// -- LibraryItem Settings (localStorage) ----------------------------------------

window.libraryItemSettings = {
    getCardSize: function () {
        return parseInt(localStorage.getItem('library-item-card-size') || '80', 10);
    },
    setCardSize: function (size) {
        localStorage.setItem('library-item-card-size', size.toString());
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
    var popupUnloadHandler = null;

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
        unregisterStateHandler: function (dotNetRef) {
            if (!dotNetRef || stateHandler === dotNetRef) {
                stateHandler = null;
            }
        },
        registerCommandHandler: function (dotNetRef) {
            commandHandler = dotNetRef;
        },
        unregisterCommandHandler: function (dotNetRef) {
            if (!dotNetRef || commandHandler === dotNetRef) {
                commandHandler = null;
            }
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
            if (popupUnloadHandler) {
                window.removeEventListener('beforeunload', popupUnloadHandler);
            }

            popupUnloadHandler = function () {
                try {
                    var json = JSON.stringify({ action: 'popup-closed' });
                    localStorage.setItem(commandKey, json);
                    if (channel) {
                        channel.postMessage({ type: 'command', json: json });
                    }
                } catch (error) {
                    console.debug('Could not broadcast listen popup close.', error);
                }
            };

            window.addEventListener('beforeunload', popupUnloadHandler);
        },
        unregisterPopupWindow: function () {
            if (!popupUnloadHandler) return;
            window.removeEventListener('beforeunload', popupUnloadHandler);
            popupUnloadHandler = null;
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
            } catch (error) {
                console.debug("Audio play request was rejected.", error);
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
