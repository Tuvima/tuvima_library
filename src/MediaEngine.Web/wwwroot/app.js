// app.js — Global JavaScript helpers for the Dashboard

/**
 * Ensures the dark theme class is applied to <body>.
 * Called from MainLayout on first render. Light mode has been removed.
 */
window.setThemeClass = function () {
    document.body.classList.add('app-dark');
};

window.tuvimaPrefersReducedMotion = function () {
    return !!(window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches);
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
    if (el.classList && el.classList.contains('media-tile-shelf-scroll') && window.updateMediaTileShelfVisibleWidth) {
        window.updateMediaTileShelfVisibleWidth(el);
    }

    var tolerance = 4;
    return {
        atStart: el.scrollLeft <= tolerance,
        atEnd: el.scrollLeft + el.clientWidth >= el.scrollWidth - tolerance
    };
};

window.getSwimlaneSnapTargets = function (el) {
    if (!el) return [];

    var maxScroll = Math.max(0, el.scrollWidth - el.clientWidth);
    var containerRect = el.getBoundingClientRect();
    var currentLeft = el.scrollLeft;
    var targets = [0, maxScroll];

    Array.prototype.forEach.call(el.children || [], function (child) {
        if (!child || child.offsetWidth <= 0) return;

        var childRect = child.getBoundingClientRect();
        var target = currentLeft + childRect.left - containerRect.left;
        target = Math.min(Math.max(target, 0), maxScroll);
        targets.push(Math.round(target));
    });

    return targets
        .filter(function (target, index, list) {
            return list.indexOf(target) === index;
        })
        .sort(function (a, b) { return a - b; });
};

window.getSwimlaneSnapTarget = function (el, direction) {
    if (!el) return 0;

    var targets = window.getSwimlaneSnapTargets(el);
    var currentLeft = el.scrollLeft;
    var maxScroll = Math.max(0, el.scrollWidth - el.clientWidth);
    var amount = el.clientWidth * 0.75;
    var tolerance = 4;

    if (targets.length === 0) {
        return Math.min(Math.max(currentLeft + (direction === 'left' ? -amount : amount), 0), maxScroll);
    }

    if (direction === 'left') {
        var desiredLeft = currentLeft - amount;
        var leftCandidates = targets.filter(function (target) {
            return target < currentLeft - tolerance;
        });
        var firstLeftCandidateInPage = leftCandidates.find(function (target) {
            return target >= desiredLeft - tolerance;
        });

        return firstLeftCandidateInPage !== undefined
            ? firstLeftCandidateInPage
            : (leftCandidates.length > 0 ? leftCandidates[leftCandidates.length - 1] : 0);
    }

    var desiredRight = currentLeft + amount;
    var rightCandidates = targets.filter(function (target) {
        return target > currentLeft + tolerance;
    });
    var lastRightCandidateInPage = rightCandidates.filter(function (target) {
        return target <= desiredRight + tolerance;
    }).pop();

    return lastRightCandidateInPage !== undefined
        ? lastRightCandidateInPage
        : (rightCandidates.length > 0 ? rightCandidates[0] : maxScroll);
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
    if (el.classList && el.classList.contains('media-tile-shelf-scroll') && window.updateMediaTileShelfVisibleWidth) {
        window.updateMediaTileShelfVisibleWidth(el);
    }

    var target = window.getSwimlaneSnapTarget(el, direction);
    el.__swimlaneAllowScroll = true;
    el.scrollTo({ left: target, behavior: 'smooth' });
    return new Promise(function (resolve) {
        setTimeout(function () {
            el.scrollLeft = target;
            el.__swimlaneStableScrollLeft = el.scrollLeft;
            el.__swimlaneAllowScroll = false;
            resolve({
                atStart: el.scrollLeft <= 4,
                atEnd: el.scrollLeft + el.clientWidth >= el.scrollWidth - 4
            });
        }, 420); // wait for smooth-scroll animation to settle
    });
};

window.updateMediaTileShelfVisibleWidth = function (el) {
    if (!el) return;

    var track = el.closest ? el.closest('.media-tile-shelf-track') : el.parentElement;
    var viewport = el.parentElement && el.parentElement.classList && el.parentElement.classList.contains('media-tile-shelf-window')
        ? el.parentElement
        : track;
    var availableWidth = viewport ? viewport.clientWidth : el.clientWidth;
    var items = Array.prototype.filter.call(el.children || [], function (child) {
        return child && child.offsetWidth > 0;
    });

    if (!availableWidth || items.length === 0) {
        return;
    }

    var firstRect = items[0].getBoundingClientRect();
    var itemWidth = firstRect.width || items[0].offsetWidth;
    if (!itemWidth) {
        return;
    }

    var style = window.getComputedStyle ? window.getComputedStyle(el) : null;
    var paddingLeft = style ? parseFloat(style.paddingLeft || '0') : 0;
    var paddingRight = style ? parseFloat(style.paddingRight || '0') : 0;
    paddingLeft = Number.isFinite(paddingLeft) ? paddingLeft : 0;
    paddingRight = Number.isFinite(paddingRight) ? paddingRight : 0;

    var visibleWidth = availableWidth;

    visibleWidth = Math.max(Math.min(availableWidth, visibleWidth), Math.min(availableWidth, itemWidth + paddingLeft + paddingRight));

    var roundedVisibleWidth = Math.round(visibleWidth);
    var arrowOffset = Math.max(0, Math.round(availableWidth - roundedVisibleWidth));
    var visibleWidthValue = roundedVisibleWidth + 'px';
    var arrowOffsetValue = arrowOffset + 'px';

    el.style.setProperty('--media-tile-shelf-visible-width', visibleWidthValue);
    el.style.setProperty('--media-tile-shelf-arrow-offset', arrowOffsetValue);

    if (track) {
        track.style.setProperty('--media-tile-shelf-visible-width', visibleWidthValue);
        track.style.setProperty('--media-tile-shelf-arrow-offset', arrowOffsetValue);
    }

    var maxScroll = Math.max(0, el.scrollWidth - el.clientWidth);
    if (el.scrollLeft > maxScroll) {
        el.scrollLeft = maxScroll;
    }

    if (el.__swimlaneStableScrollLeft !== undefined) {
        el.__swimlaneStableScrollLeft = Math.min(el.__swimlaneStableScrollLeft, maxScroll);
    }
};

window.isVerticalMediaTileWheel = function (event) {
    if (!event) return false;
    return Math.abs(event.deltaY || 0) >= Math.abs(event.deltaX || 0);
};

window.registerMediaTileShelfScrollGuard = function (el) {
    if (!el || el.__mediaTileShelfScrollGuard) return;

    var isFinePointer = function () {
        return !window.matchMedia || window.matchMedia('(hover: hover) and (pointer: fine)').matches;
    };

    var restoreStablePosition = function () {
        if (!isFinePointer()) return;

        if (!el.__swimlaneAllowScroll && !el.__mediaTileHoverScrollLock) {
            var stableLeft = el.__swimlaneStableScrollLeft || 0;
            if (Math.abs(el.scrollLeft - stableLeft) > 1) {
                el.scrollLeft = stableLeft;
            }
        }

        if (Math.abs((el.scrollTop || 0)) > 1) {
            el.scrollTop = 0;
        }
    };

    var onWheel = function (event) {
        if (!isFinePointer()) return;

        if (window.isVerticalMediaTileWheel(event)) {
            window.requestAnimationFrame(restoreStablePosition);
            return;
        }

        event.preventDefault();
        event.stopPropagation();
        window.requestAnimationFrame(restoreStablePosition);
    };

    var onScroll = function () {
        window.requestAnimationFrame(restoreStablePosition);
    };

    var onResize = function () {
        if (el.__mediaTileShelfResizeFrame) {
            window.cancelAnimationFrame(el.__mediaTileShelfResizeFrame);
        }

        el.__mediaTileShelfResizeFrame = window.requestAnimationFrame(function () {
            el.__mediaTileShelfResizeFrame = null;
            window.updateMediaTileShelfVisibleWidth(el);
            restoreStablePosition();
        });
    };

    window.updateMediaTileShelfVisibleWidth(el);
    el.__swimlaneStableScrollLeft = el.scrollLeft;
    el.__mediaTileShelfScrollGuard = {
        onWheel: onWheel,
        onScroll: onScroll,
        onResize: onResize
    };
    el.classList.add('is-row-scroll-guarded');
    el.addEventListener('wheel', onWheel, { passive: false });
    el.addEventListener('scroll', onScroll, { passive: true });
    window.addEventListener('resize', onResize, { passive: true });
};

window.unregisterMediaTileShelfScrollGuard = function (el) {
    if (!el || !el.__mediaTileShelfScrollGuard) return;

    el.removeEventListener('wheel', el.__mediaTileShelfScrollGuard.onWheel);
    el.removeEventListener('scroll', el.__mediaTileShelfScrollGuard.onScroll);
    window.removeEventListener('resize', el.__mediaTileShelfScrollGuard.onResize);

    if (el.__mediaTileShelfResizeFrame) {
        window.cancelAnimationFrame(el.__mediaTileShelfResizeFrame);
        el.__mediaTileShelfResizeFrame = null;
    }

    var track = el.closest ? el.closest('.media-tile-shelf-track') : el.parentElement;
    if (track) {
        track.style.removeProperty('--media-tile-shelf-visible-width');
        track.style.removeProperty('--media-tile-shelf-arrow-offset');
    }

    el.style.removeProperty('--media-tile-shelf-visible-width');
    el.style.removeProperty('--media-tile-shelf-arrow-offset');
    el.classList.remove('is-row-scroll-guarded');
    el.__mediaTileShelfScrollGuard = null;
    el.__swimlaneAllowScroll = false;
};

// -- Media tile hover positioning ------------------------------------

window.getMediaTileHoverHost = function () {
    var host = document.getElementById('media-tile-hover-host');
    if (host) return host;

    host = document.createElement('div');
    host.id = 'media-tile-hover-host';
    host.className = 'media-tile-hover-host';
    document.body.appendChild(host);
    return host;
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

        var frame = cardEl.querySelector('.media-tile-frame') || cardEl;
        var cardRect = frame.getBoundingClientRect();
        var viewportWidth = document.documentElement.clientWidth || window.innerWidth;
        var viewportHeight = document.documentElement.clientHeight || window.innerHeight;
        var gutter = 12;
        panel.style.removeProperty('--media-tile-hover-left');
        panel.style.removeProperty('--media-tile-hover-top');
        panel.style.removeProperty('--media-tile-hover-max-height');
        panel.style.removeProperty('--media-tile-hover-art-max-height');
        panel.style.setProperty('--media-tile-hover-anchor-width', Math.round(cardRect.width) + 'px');
        panel.style.setProperty('--media-tile-hover-anchor-height', Math.round(cardRect.height) + 'px');
        panel.style.setProperty('--media-tile-hover-max-height', Math.round(cardRect.height) + 'px');
        panel.style.left = '';
        panel.style.top = '';

        var body = panel.querySelector('.media-tile-hover-body');
        var bodyHeight = body ? body.getBoundingClientRect().height : 0;
        panel.style.setProperty('--media-tile-hover-art-max-height', Math.round(cardRect.height) + 'px');

        var panelRect = panel.getBoundingClientRect();
        var panelStyle = window.getComputedStyle ? window.getComputedStyle(panel) : null;
        var panelWidth = panelRect.width || panel.offsetWidth;
        var panelHeight = panelRect.height || panel.offsetHeight;
        if (!panelWidth) {
            panelWidth = cardRect.width;
        }
        if (!panelHeight) {
            panelHeight = cardRect.height;
        }

        var estimatedPanelHeight = panelHeight;
        if (panelWidth > 0 && panel.classList.contains('is-banner-popover')) {
            estimatedPanelHeight = Math.max(estimatedPanelHeight, (panelWidth * 9 / 16) + bodyHeight + 8);
        } else if (panelWidth > 0 && panel.classList.contains('is-art-popover')) {
            var estimatedArtHeight = panelWidth;
            if (panel.classList.contains('is-portrait')) {
                estimatedArtHeight = panelWidth * 1.5;
            }

            estimatedPanelHeight = Math.max(estimatedPanelHeight, estimatedArtHeight + bodyHeight + 8);
        }

        var rawPanelMaxHeight = panelStyle ? parseFloat(panelStyle.maxHeight || '0') : 0;
        if (Number.isFinite(rawPanelMaxHeight) && rawPanelMaxHeight > 0) {
            estimatedPanelHeight = Math.min(estimatedPanelHeight, rawPanelMaxHeight);
        }

        panelHeight = Math.max(panelHeight, Math.min(estimatedPanelHeight, viewportHeight - (gutter * 2)));

        // Expand from the resting card's centre, like a streaming-service preview,
        // while keeping the shelf itself completely stationary.
        var cardCenterX = cardRect.left + (cardRect.width / 2);
        var panelLeft = cardCenterX - (panelWidth / 2);
        var minLeft = gutter;
        var maxLeft = viewportWidth - gutter - panelWidth;
        if (maxLeft < minLeft) {
            panelLeft = minLeft;
        } else {
            panelLeft = Math.min(Math.max(panelLeft, minLeft), maxLeft);
        }

        var panelTop = cardRect.top;

        panel.style.setProperty('--media-tile-hover-left', Math.round(panelLeft) + 'px');
        panel.style.setProperty('--media-tile-hover-top', Math.round(panelTop) + 'px');
        panel.style.setProperty('--media-tile-hover-origin-x', Math.round(cardCenterX - panelLeft) + 'px');
        panel.classList.add('is-positioned');

        if (cardEl.__mediaTileHoverNeedsReposition) {
            cardEl.__mediaTileHoverNeedsReposition = false;
            window.positionMediaTileHover(cardEl);
        }
    });
};

window.correctMediaTileHoverViewport = function (cardEl) {
    if (!cardEl) return;

    var panel = cardEl.__mediaTileHoverPanel || cardEl.querySelector('.media-tile-hover-panel');
    if (!panel || !panel.classList.contains('is-visible')) return;

    window.positionMediaTileHover(cardEl);
};

window.scheduleMediaTileHoverViewportCorrection = function (cardEl) {
    window.requestAnimationFrame(function () {
        window.correctMediaTileHoverViewport(cardEl);
        window.requestAnimationFrame(function () {
            window.correctMediaTileHoverViewport(cardEl);
        });
    });
};

window.mountMediaTileHover = function (cardEl) {
    if (!cardEl) return null;

    var panel = cardEl.__mediaTileHoverPanel || cardEl.querySelector('.media-tile-hover-panel');
    if (!panel) return null;

    if (!panel.__mediaTileHoverOriginalParent) {
        panel.__mediaTileHoverOriginalParent = panel.parentElement;
    }

    if (!Object.prototype.hasOwnProperty.call(panel, '__mediaTileHoverOriginalNextSibling')) {
        panel.__mediaTileHoverOriginalNextSibling = panel.nextSibling;
    }

    if (window.getComputedStyle) {
        var cardStyle = window.getComputedStyle(cardEl);
        [
            '--media-tile-accent',
            '--media-tile-secondary-accent',
            '--media-tile-action-accent',
            '--media-tile-progress-accent',
            '--art-bg-base',
            '--art-bg-base-dark',
            '--art-bg-accent',
            '--art-bg-accent-muted',
            '--art-bg-glow',
            '--art-bg-glow-secondary',
            '--art-bg-border',
            '--art-bg-shadow',
            '--art-bg-overlay',
            '--media-tile-hover-image'
        ].forEach(function (propertyName) {
            var propertyValue = cardStyle.getPropertyValue(propertyName);
            if (propertyValue) {
                panel.style.setProperty(propertyName, propertyValue.trim());
            }
        });
    }

    var mountParent = window.getMediaTileHoverHost();
    if (mountParent && panel.parentElement !== mountParent) {
        mountParent.appendChild(panel);
    }

    panel.classList.remove('is-visible');
    panel.classList.remove('is-positioned');
    panel.classList.add('is-viewport-mounted');
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

    panel.classList.remove('is-viewport-mounted');
    panel.classList.remove('is-positioned');
};

window.lockMediaTileHoverRowScroll = function (cardEl) {
    if (!cardEl) return;

    var scrollEl = cardEl.closest('.media-tile-shelf-scroll');
    if (!scrollEl) return;

    cardEl.__mediaTileHoverScrollElement = scrollEl;

    if (!scrollEl.__mediaTileHoverScrollLock) {
        scrollEl.__mediaTileHoverScrollLock = {
            owner: cardEl,
            left: scrollEl.scrollLeft,
            top: scrollEl.scrollTop || 0
        };

        var restore = function () {
            var lock = scrollEl.__mediaTileHoverScrollLock;
            if (!lock) return;
            if (scrollEl.__swimlaneAllowScroll) return;

            if (scrollEl.scrollLeft !== lock.left) {
                scrollEl.scrollLeft = lock.left;
            }

            if ((scrollEl.scrollTop || 0) !== lock.top) {
                scrollEl.scrollTop = lock.top;
            }
        };

        var blockWheel = function (event) {
            var lock = scrollEl.__mediaTileHoverScrollLock;
            if (!lock) return;

            if (window.isVerticalMediaTileWheel(event)) {
                window.requestAnimationFrame(restore);
                return;
            }

            event.preventDefault();
            event.stopPropagation();

            window.requestAnimationFrame(restore);
        };

        scrollEl.__mediaTileHoverScrollRestore = restore;
        scrollEl.__mediaTileHoverWheelBlock = blockWheel;
        scrollEl.addEventListener('scroll', restore, { passive: true });
        scrollEl.addEventListener('wheel', blockWheel, { passive: false });
    } else {
        scrollEl.__mediaTileHoverScrollLock.owner = cardEl;
        scrollEl.__mediaTileHoverScrollLock.left = scrollEl.scrollLeft;
        scrollEl.__mediaTileHoverScrollLock.top = scrollEl.scrollTop || 0;
    }

    scrollEl.classList.add('is-hover-scroll-locked');

    var panel = cardEl.__mediaTileHoverPanel || cardEl.querySelector('.media-tile-hover-panel');
    if (panel && !panel.__mediaTileHoverWheelBlock) {
        panel.__mediaTileHoverWheelBlock = function (event) {
            var lock = scrollEl.__mediaTileHoverScrollLock;
            if (lock) {
                window.requestAnimationFrame(function () {
                    if (scrollEl.scrollLeft !== lock.left && !scrollEl.__swimlaneAllowScroll) {
                        scrollEl.scrollLeft = lock.left;
                    }

                    if ((scrollEl.scrollTop || 0) !== lock.top) {
                        scrollEl.scrollTop = lock.top;
                    }
                });
            }

            if (window.isVerticalMediaTileWheel(event)) {
                return;
            }

            event.preventDefault();
            event.stopPropagation();
        };
        panel.addEventListener('wheel', panel.__mediaTileHoverWheelBlock, { passive: false });
    }
};

window.unlockMediaTileHoverRowScroll = function (cardEl) {
    if (!cardEl) return;

    var scrollEl = cardEl.__mediaTileHoverScrollElement;
    if (!scrollEl || !scrollEl.__mediaTileHoverScrollLock) return;

    if (scrollEl.__mediaTileHoverScrollLock.owner !== cardEl) return;

    if (scrollEl.__mediaTileHoverScrollRestore) {
        scrollEl.removeEventListener('scroll', scrollEl.__mediaTileHoverScrollRestore);
    }
    if (scrollEl.__mediaTileHoverWheelBlock) {
        scrollEl.removeEventListener('wheel', scrollEl.__mediaTileHoverWheelBlock);
    }

    var panel = cardEl.__mediaTileHoverPanel || cardEl.querySelector('.media-tile-hover-panel');
    if (panel && panel.__mediaTileHoverWheelBlock) {
        panel.removeEventListener('wheel', panel.__mediaTileHoverWheelBlock);
        panel.__mediaTileHoverWheelBlock = null;
    }

    scrollEl.classList.remove('is-hover-scroll-locked');
    scrollEl.__mediaTileHoverScrollLock = null;
    scrollEl.__mediaTileHoverScrollRestore = null;
    scrollEl.__mediaTileHoverWheelBlock = null;
    cardEl.__mediaTileHoverScrollElement = null;
};

window.showMediaTileHover = function (cardEl) {
    if (!cardEl) return;

    var rowContainer = cardEl.closest('.media-tile-shelf-scroll, .media-tile-grid');
    var previousCard = rowContainer
        ? rowContainer.querySelector('.media-tile.is-hover-active')
        : null;
    if (previousCard && previousCard !== cardEl) {
        window.clearMediaTileHover(previousCard);
    }

    var panel = cardEl.querySelector('.media-tile-hover-panel');
    if (!panel) return;

    cardEl.__mediaTileHoverPanel = panel;

    if (cardEl.__mediaTileHideTimer) {
        window.clearTimeout(cardEl.__mediaTileHideTimer);
        cardEl.__mediaTileHideTimer = null;
    }

    var frame = cardEl.querySelector('.media-tile-frame') || cardEl;
    var frameRect = frame.getBoundingClientRect();
    cardEl.style.setProperty('--media-tile-hover-anchor-width', Math.round(frameRect.width) + 'px');
    cardEl.style.setProperty('--media-tile-hover-anchor-height', Math.round(frameRect.height) + 'px');
    cardEl.classList.add('is-hover-active');
    panel.classList.add('is-inline-expanded');

    var hoverImage = panel.querySelector('.media-tile-hover-image');
    if (hoverImage) {
        hoverImage.loading = 'eager';
        if ('fetchPriority' in hoverImage) {
            hoverImage.fetchPriority = 'high';
        }
    }

    window.requestAnimationFrame(function () {
        window.requestAnimationFrame(function () {
            if (cardEl.classList.contains('is-hover-active')) {
                panel.classList.add('is-visible');
            }
        });
    });
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
    panel.classList.remove('is-inline-expanded');

    window.setTimeout(function () {
        if (cardEl.classList.contains('is-hover-active') || panel.classList.contains('is-visible')) {
            return;
        }

        cardEl.style.removeProperty('--media-tile-hover-anchor-width');
        cardEl.style.removeProperty('--media-tile-hover-anchor-height');
    }, 340);
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

    cardEl.classList.add('is-hover-js-enabled');

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
        }, 280);
    };

    var isWithinHoverSurface = function (target) {
        if (!target) return false;

        var activePanel = cardEl.__mediaTileHoverPanel || panel;
        return cardEl.contains(target) || (activePanel && activePanel.contains(target));
    };

    var isHoverSurfaceStillActive = function () {
        var activePanel = cardEl.__mediaTileHoverPanel || panel;
        var activeElement = document.activeElement;

        return (cardEl.matches && cardEl.matches(':hover'))
            || (activePanel && activePanel.matches && activePanel.matches(':hover'))
            || isWithinHoverSurface(activeElement);
    };

    var scheduleHide = function (event) {
        if (cardEl.__mediaTilePinned) return;
        if (event && isWithinHoverSurface(event.relatedTarget)) return;

        if (cardEl.__mediaTileShowTimer) {
            window.clearTimeout(cardEl.__mediaTileShowTimer);
            cardEl.__mediaTileShowTimer = null;
        }

        if (cardEl.__mediaTileHideTimer) {
            window.clearTimeout(cardEl.__mediaTileHideTimer);
        }

        cardEl.__mediaTileHideTimer = window.setTimeout(function () {
            if (isHoverSurfaceStillActive()) {
                return;
            }

            window.clearMediaTileHover(cardEl);
        }, 180);
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

    cardEl.__mediaTileHoverShow = show;
    cardEl.__mediaTileHoverHide = scheduleHide;
    cardEl.__mediaTileHoverKeepOpen = keepOpen;
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

    if (cardEl.__mediaTileHideTimer) {
        window.clearTimeout(cardEl.__mediaTileHideTimer);
    }
    if (cardEl.__mediaTileShowTimer) {
        window.clearTimeout(cardEl.__mediaTileShowTimer);
    }
    cardEl.__mediaTilePinned = false;
    cardEl.classList.remove('is-hover-js-enabled');
    window.clearMediaTileHover(cardEl);

    delete cardEl.__mediaTileHoverPanel;
    delete cardEl.__mediaTileHoverShow;
    delete cardEl.__mediaTileHoverHide;
    delete cardEl.__mediaTileHoverKeepOpen;
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
    var playbackConfig = {
        popupWidth: 460,
        popupHeight: 820,
        immediateActionDedupMilliseconds: 900,
        immediateActionConsumeMilliseconds: 1800,
        audioObserverIntervalMilliseconds: 1200,
        audioObserverMinimumIntervalMilliseconds: 500,
        seekToleranceSeconds: 0.75,
        volumeStep: 0.05,
        defaultVolume: 0.8
    };
    var stateKey = 'tuvima.playback.v2.state';
    var commandKey = 'tuvima.playback.v2.command';
    var deviceIdKey = 'tuvima.playback.v2.device-id';
    var popupName = 'tuvima-listen-mini-player';
    var channel = typeof BroadcastChannel !== 'undefined' ? new BroadcastChannel('tuvima-listen-playback') : null;
    var stateHandler = null;
    var commandHandler = null;
    var popupWindow = null;
    var popupUnloadHandler = null;
    var audioObservers = typeof WeakMap !== 'undefined' ? new WeakMap() : null;
    var playerShortcutHandlers = typeof WeakMap !== 'undefined' ? new WeakMap() : null;

    function readConfigValue(options, camelName, snakeName, fallback) {
        if (!options) return fallback;
        if (Object.prototype.hasOwnProperty.call(options, camelName)) return options[camelName];
        var pascalName = camelName.charAt(0).toUpperCase() + camelName.slice(1);
        if (Object.prototype.hasOwnProperty.call(options, pascalName)) return options[pascalName];
        if (snakeName && Object.prototype.hasOwnProperty.call(options, snakeName)) return options[snakeName];
        return fallback;
    }

    function asFiniteNumber(value, fallback, min, max) {
        var next = typeof value === 'number' ? value : Number.parseFloat(value);
        if (!Number.isFinite(next)) return fallback;
        if (typeof min === 'number') next = Math.max(min, next);
        if (typeof max === 'number') next = Math.min(max, next);
        return next;
    }

    function configure(options) {
        playbackConfig.popupWidth = asFiniteNumber(readConfigValue(options, 'popupWidth', 'popup_width', playbackConfig.popupWidth), playbackConfig.popupWidth, 280, 1200);
        playbackConfig.popupHeight = asFiniteNumber(readConfigValue(options, 'popupHeight', 'popup_height', playbackConfig.popupHeight), playbackConfig.popupHeight, 360, 1400);
        playbackConfig.immediateActionDedupMilliseconds = asFiniteNumber(readConfigValue(options, 'immediateActionDedupMilliseconds', 'immediate_action_dedup_milliseconds', playbackConfig.immediateActionDedupMilliseconds), playbackConfig.immediateActionDedupMilliseconds, 100, 5000);
        playbackConfig.immediateActionConsumeMilliseconds = asFiniteNumber(readConfigValue(options, 'immediateActionConsumeMilliseconds', 'immediate_action_consume_milliseconds', playbackConfig.immediateActionConsumeMilliseconds), playbackConfig.immediateActionConsumeMilliseconds, 100, 10000);
        playbackConfig.audioObserverIntervalMilliseconds = asFiniteNumber(readConfigValue(options, 'audioObserverIntervalMilliseconds', 'audio_observer_interval_milliseconds', playbackConfig.audioObserverIntervalMilliseconds), playbackConfig.audioObserverIntervalMilliseconds, 100, 10000);
        playbackConfig.audioObserverMinimumIntervalMilliseconds = asFiniteNumber(readConfigValue(options, 'audioObserverMinimumIntervalMilliseconds', 'audio_observer_minimum_interval_milliseconds', playbackConfig.audioObserverMinimumIntervalMilliseconds), playbackConfig.audioObserverMinimumIntervalMilliseconds, 100, 5000);
        playbackConfig.seekToleranceSeconds = asFiniteNumber(readConfigValue(options, 'seekToleranceSeconds', 'seek_tolerance_seconds', playbackConfig.seekToleranceSeconds), playbackConfig.seekToleranceSeconds, 0.05, 10);
        playbackConfig.volumeStep = asFiniteNumber(readConfigValue(options, 'volumeStep', 'volume_step', playbackConfig.volumeStep), playbackConfig.volumeStep, 0.01, 0.5);
        playbackConfig.defaultVolume = asFiniteNumber(readConfigValue(options, 'defaultVolume', 'default_volume', playbackConfig.defaultVolume), playbackConfig.defaultVolume, 0, 1);
    }

    function createDeviceId() {
        if (window.crypto && typeof window.crypto.randomUUID === 'function') {
            return window.crypto.randomUUID();
        }

        return 'web-' + Date.now().toString(36) + '-' + Math.random().toString(36).slice(2, 12);
    }

    function getOrCreateDeviceId() {
        var existing = localStorage.getItem(deviceIdKey);
        if (existing) return existing;

        var next = createDeviceId();
        localStorage.setItem(deviceIdKey, next);
        return next;
    }

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

    function readAudioElementState(element) {
        if (!element) {
            return {
                currentTime: 0,
                duration: 0,
                volume: playbackConfig.defaultVolume,
                muted: false,
                paused: true,
                playbackRate: 1
            };
        }

        var pendingStart = Number.parseFloat(element.dataset.listenPendingStartPosition || '');
        var currentTime = element.currentTime || 0;
        if (Number.isFinite(pendingStart) && pendingStart > 0) {
            var pendingStartedAt = Number.parseFloat(element.dataset.listenPendingStartAt || '');
            var pendingAge = Number.isFinite(pendingStartedAt) ? Date.now() - pendingStartedAt : 0;
            if (currentTime >= pendingStart - playbackConfig.seekToleranceSeconds || pendingAge > 8000) {
                delete element.dataset.listenPendingStartPosition;
                delete element.dataset.listenPendingStartAt;
            } else {
                currentTime = pendingStart;
            }
        }

        return {
            currentTime: currentTime,
            duration: isFinite(element.duration) ? element.duration : 0,
            volume: typeof element.volume === 'number' ? element.volume : playbackConfig.defaultVolume,
            muted: !!element.muted,
            paused: !!element.paused,
            playbackRate: typeof element.playbackRate === 'number' ? element.playbackRate : 1
        };
    }

    function audioObserverFor(element) {
        return audioObservers ? audioObservers.get(element) : element && element.__listenAudioObserver;
    }

    function setAudioObserver(element, observer) {
        if (!element) return;
        if (audioObservers) {
            if (observer) {
                audioObservers.set(element, observer);
            } else {
                audioObservers.delete(element);
            }
            return;
        }

        if (observer) {
            element.__listenAudioObserver = observer;
        } else {
            delete element.__listenAudioObserver;
        }
    }

    function removeAudioObserver(element) {
        var observer = audioObserverFor(element);
        if (!element || !observer) return;

        element.removeEventListener('timeupdate', observer.onTimeUpdate);
        element.removeEventListener('durationchange', observer.onMetadataChanged);
        element.removeEventListener('loadedmetadata', observer.onMetadataChanged);
        element.removeEventListener('ratechange', observer.onMetadataChanged);
        element.removeEventListener('volumechange', observer.onMetadataChanged);
        setAudioObserver(element, null);
    }

    function shortcutHandlerFor(element) {
        return playerShortcutHandlers ? playerShortcutHandlers.get(element) : element && element.__listenPlaybackShortcutHandler;
    }

    function setShortcutHandler(element, handler) {
        if (!element) return;
        if (playerShortcutHandlers) {
            if (handler) {
                playerShortcutHandlers.set(element, handler);
            } else {
                playerShortcutHandlers.delete(element);
            }
            return;
        }

        if (handler) {
            element.__listenPlaybackShortcutHandler = handler;
        } else {
            delete element.__listenPlaybackShortcutHandler;
        }
    }

    function isEditableShortcutTarget(target) {
        if (!target) return false;
        var tagName = (target.tagName || '').toLowerCase();
        if (tagName === 'input' || tagName === 'textarea' || tagName === 'select') return true;
        if (target.isContentEditable) return true;
        return !!(target.closest && target.closest('[contenteditable="true"]'));
    }

    function unregisterPlayerShortcuts(element) {
        var handler = shortcutHandlerFor(element);
        if (!element || !handler) return;
        element.removeEventListener('keydown', handler);
        setShortcutHandler(element, null);
    }

    function registerPlayerShortcuts(element, dotNetRef) {
        if (!element || !dotNetRef) return;
        unregisterPlayerShortcuts(element);

        var handler = function (event) {
            if (!event || isEditableShortcutTarget(event.target)) return;

            var action = null;
            if (event.code === 'Space' || event.key === ' ') action = 'toggle-play';
            if (event.key === 'ArrowLeft') action = 'skip-back';
            if (event.key === 'ArrowRight') action = 'skip-forward';
            if (event.key === 'ArrowUp') action = 'volume-up';
            if (event.key === 'ArrowDown') action = 'volume-down';
            if (!action) return;

            event.preventDefault();
            dotNetRef.invokeMethodAsync('HandlePlayerShortcut', action)
                .catch(function (error) {
                    console.debug('Could not dispatch playback shortcut.', error);
                });
        };

        element.addEventListener('keydown', handler);
        setShortcutHandler(element, handler);
    }

    function audioEngineElement() {
        var element = document.getElementById('listen-audio-engine');
        if (element) return element;

        try {
            if (window.opener && !window.opener.closed && window.opener.document) {
                return window.opener.document.getElementById('listen-audio-engine');
            }
        } catch (error) {
            console.debug("Could not access opener audio engine.", error);
        }

        return null;
    }

    async function startAudioElement(element, options) {
        if (!element) return false;

        var payload = options || {};
        var streamUrl = payload.streamUrl || payload.StreamUrl || '';
        var positionSeconds = payload.positionSeconds ?? payload.PositionSeconds ?? 0;
        var playbackRate = payload.playbackRate ?? payload.PlaybackRate ?? 1;
        var volume = payload.volume ?? payload.Volume;
        var muted = payload.muted ?? payload.Muted;

        try {
            if (streamUrl && element.getAttribute('src') !== streamUrl) {
                element.setAttribute('src', streamUrl);
                element.load();
            } else if (streamUrl && element.readyState === 0) {
                element.load();
            }

            if (typeof volume === 'number') {
                element.volume = Math.max(0, Math.min(1, volume));
            }

            if (typeof muted === 'boolean') {
                element.muted = muted;
            }

            if (typeof playbackRate === 'number' && isFinite(playbackRate)) {
                element.playbackRate = playbackRate;
            }

            var target = Math.max(0, positionSeconds || 0);
            if (target > 0) {
                element.dataset.listenPendingStartPosition = String(target);
                element.dataset.listenPendingStartAt = String(Date.now());
            } else {
                delete element.dataset.listenPendingStartPosition;
                delete element.dataset.listenPendingStartAt;
            }

            var applyTargetSeek = function () {
                if (target <= 0) return;
                try {
                    element.currentTime = target;
                } catch (seekError) {
                    console.debug("Audio start seek was rejected.", seekError);
                }
            };

            if (target > 0 && element.readyState >= 1) {
                applyTargetSeek();
            } else if (target > 0) {
                element.addEventListener('loadedmetadata', applyTargetSeek, { once: true });
            }

            await element.play();
            if (target > 0 && element.readyState >= 1 && Math.abs((element.currentTime || 0) - target) > playbackConfig.seekToleranceSeconds) {
                applyTargetSeek();
            }
            if (target <= 0 || (element.currentTime || 0) >= target - playbackConfig.seekToleranceSeconds) {
                delete element.dataset.listenPendingStartPosition;
                delete element.dataset.listenPendingStartAt;
            }
            return true;
        } catch (error) {
            console.debug("Audio start request was rejected.", error);
            return false;
        }
    }

    function ensureAudioSource(element, streamUrl) {
        if (!element || !streamUrl) return;

        try {
            if (element.getAttribute('src') !== streamUrl) {
                element.setAttribute('src', streamUrl);
                element.load();
            }
        } catch (error) {
            console.debug("Audio source sync was rejected.", error);
        }
    }

    function numberFromDataset(value, fallback) {
        var parsed = Number.parseFloat(value);
        return Number.isFinite(parsed) ? parsed : fallback;
    }

    function boolFromDataset(value, fallback) {
        if (value === 'true') return true;
        if (value === 'false') return false;
        return fallback;
    }

    function startPositionFromDataset(action, fallback) {
        var position = numberFromDataset(action.dataset.listenStartPosition, fallback || 0);
        if (position > 0) return position;

        if ((action.dataset.listenStartKind || '').toLowerCase() !== 'resume') {
            return Math.max(0, position || 0);
        }

        var progress = numberFromDataset(action.dataset.listenStartProgress, 0);
        var duration = numberFromDataset(action.dataset.listenStartDuration, 0);
        var rewind = numberFromDataset(action.dataset.listenStartRewind, 0);
        if (progress > 0 && progress < 99.5 && duration > 0) {
            return Math.max(0, duration * Math.min(100, progress) / 100 - Math.max(0, rewind));
        }

        return Math.max(0, position || 0);
    }

    function openPopupWindow(url) {
        if (!url) return false;

        popupWindow = window.open(
            url,
            popupName,
            'popup=yes,width=' + Math.round(playbackConfig.popupWidth) + ',height=' + Math.round(playbackConfig.popupHeight) + ',resizable=yes,scrollbars=no'
        );

        if (popupWindow && typeof popupWindow.focus === 'function') {
            popupWindow.focus();
        }

        return !!popupWindow;
    }

    function focusPopup() {
        window.requestAnimationFrame(function () {
            var target = document.querySelector('.listen-popup button:not([disabled]), .listen-popup input:not([disabled]), main button:not([disabled]), main input:not([disabled])');
            if (target && typeof target.focus === 'function') {
                target.focus({ preventScroll: true });
            }
        });
    }

    var lastImmediateStartAction = null;
    var lastImmediateStartAt = 0;
    var lastImmediatePopupAction = null;
    var lastImmediatePopupAt = 0;

    function startAudioFromImmediateAction(target, allowDuplicate) {
        var action = target && target.closest ? target.closest('[data-listen-immediate-start="audiobook"]') : null;
        if (!action || action.disabled || action.getAttribute('aria-disabled') === 'true') return;

        var audio = audioEngineElement();
        if (!audio) return;

        var now = Date.now();
        if (!allowDuplicate && action === lastImmediateStartAction && now - lastImmediateStartAt < playbackConfig.immediateActionDedupMilliseconds) {
            return;
        }

        lastImmediateStartAction = action;
        lastImmediateStartAt = now;

        startAudioElement(audio, {
            streamUrl: action.dataset.listenStartUrl || '',
            positionSeconds: startPositionFromDataset(action, 0),
            playbackRate: numberFromDataset(action.dataset.listenStartRate, 1),
            volume: numberFromDataset(action.dataset.listenStartVolume, undefined),
            muted: boolFromDataset(action.dataset.listenStartMuted, undefined)
        });
    }

    var lastImmediateToggleAction = null;
    var lastImmediateToggleAt = 0;
    var immediateToggleConsumedAt = 0;

    function toggleAudioFromImmediateAction(target, allowDuplicate) {
        var action = target && target.closest ? target.closest('[data-listen-audio-toggle="true"]') : null;
        if (!action || action.disabled || action.getAttribute('aria-disabled') === 'true') return;

        var audio = audioEngineElement();
        if (!audio) return;

        var now = Date.now();
        if (!allowDuplicate && action === lastImmediateToggleAction && now - lastImmediateToggleAt < playbackConfig.immediateActionDedupMilliseconds) {
            return;
        }

        lastImmediateToggleAction = action;
        lastImmediateToggleAt = now;

        if (!audio.paused) {
            audio.pause();
            immediateToggleConsumedAt = now;
            return;
        }

        var start = startAudioElement(audio, {
            streamUrl: action.dataset.listenStartUrl || audio.getAttribute('src') || '',
            positionSeconds: startPositionFromDataset(action, audio.currentTime || 0),
            playbackRate: numberFromDataset(action.dataset.listenStartRate, audio.playbackRate || 1),
            volume: numberFromDataset(action.dataset.listenStartVolume, undefined),
            muted: boolFromDataset(action.dataset.listenStartMuted, undefined)
        });
        if (start && typeof start.then === 'function') {
            start.then(function (started) {
                if (started) {
                    immediateToggleConsumedAt = Date.now();
                }
            });
        }
    }

    var lastImmediateSeekAction = null;
    var lastImmediateSeekAt = 0;
    var immediateSeekConsumedAt = 0;

    function seekAudioFromImmediateAction(target, allowDuplicate) {
        var action = target && target.closest ? target.closest('[data-playback-seek-delta]') : null;
        if (!action || action.disabled || action.getAttribute('aria-disabled') === 'true') return;

        var audio = audioEngineElement();
        if (!audio) return;

        var now = Date.now();
        if (!allowDuplicate && action === lastImmediateSeekAction && now - lastImmediateSeekAt < playbackConfig.immediateActionDedupMilliseconds) {
            return;
        }

        var delta = numberFromDataset(action.dataset.playbackSeekDelta, 0);
        if (!delta) return;

        lastImmediateSeekAction = action;
        lastImmediateSeekAt = now;

        var duration = Number.isFinite(audio.duration) && audio.duration > 0 ? audio.duration : Number.MAX_SAFE_INTEGER;
        var current = Number.isFinite(audio.currentTime) ? audio.currentTime : 0;
        var next = Math.max(0, Math.min(duration, current + delta));
        try {
            audio.currentTime = next;
            immediateSeekConsumedAt = now;
        } catch (error) {
            console.debug("Immediate audio seek was rejected.", error);
        }
    }

    function openPopupFromImmediateAction(target, allowDuplicate) {
        var action = target && target.closest ? target.closest('[data-listen-popup-route]') : null;
        if (!action || action.disabled || action.getAttribute('aria-disabled') === 'true') return;

        var route = action.dataset.listenPopupRoute || '';
        if (!route) return;

        var now = Date.now();
        if (!allowDuplicate && action === lastImmediatePopupAction && now - lastImmediatePopupAt < playbackConfig.immediateActionDedupMilliseconds) {
            return;
        }

        lastImmediatePopupAction = action;
        lastImmediatePopupAt = now;
        openPopupWindow(route);
    }

    document.addEventListener('click', function (event) {
        startAudioFromImmediateAction(event.target, true);
        toggleAudioFromImmediateAction(event.target, false);
        seekAudioFromImmediateAction(event.target, false);
        openPopupFromImmediateAction(event.target, false);
    }, true);

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
        configure: configure,
        getOrCreateDeviceId: getOrCreateDeviceId,
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
        openPopup: openPopupWindow,
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
        focusPopup: focusPopup,
        unregisterPopupWindow: function () {
            if (!popupUnloadHandler) return;
            window.removeEventListener('beforeunload', popupUnloadHandler);
            popupUnloadHandler = null;
        },
        closeOwnWindow: function () {
            window.close();
        },
        readAudioState: function (element) {
            return readAudioElementState(element);
        },
        registerAudioStateObserver: function (element, dotNetRef, intervalMs) {
            if (!element || !dotNetRef) return;
            removeAudioObserver(element);

            var interval = Math.max(
                playbackConfig.audioObserverMinimumIntervalMilliseconds,
                intervalMs || playbackConfig.audioObserverIntervalMilliseconds);
            var lastNotifiedAt = 0;
            var notify = function (force) {
                var now = Date.now();
                if (!force && now - lastNotifiedAt < interval) {
                    return;
                }

                lastNotifiedAt = now;
                try {
                    var invocation = dotNetRef.invokeMethodAsync('HandleObservedAudioState', readAudioElementState(element));
                    if (invocation && typeof invocation.catch === 'function') {
                        invocation.catch(function (error) {
                            console.debug('Could not report listen audio state.', error);
                        });
                    }
                } catch (error) {
                    console.debug('Could not report listen audio state.', error);
                }
            };
            var observer = {
                onTimeUpdate: function () { notify(false); },
                onMetadataChanged: function () { notify(true); }
            };

            element.addEventListener('timeupdate', observer.onTimeUpdate);
            element.addEventListener('durationchange', observer.onMetadataChanged);
            element.addEventListener('loadedmetadata', observer.onMetadataChanged);
            element.addEventListener('ratechange', observer.onMetadataChanged);
            element.addEventListener('volumechange', observer.onMetadataChanged);
            setAudioObserver(element, observer);
            notify(true);
        },
        unregisterAudioStateObserver: function (element) {
            removeAudioObserver(element);
        },
        registerPlayerShortcuts: registerPlayerShortcuts,
        unregisterPlayerShortcuts: unregisterPlayerShortcuts,
        loadAudio: function (element) {
            if (!element) return;
            try {
                element.load();
            } catch (error) {
                console.debug("Audio load was rejected.", error);
            }
        },
        ensureAudioSource: ensureAudioSource,
        consumeImmediateToggleHandled: function () {
            var now = Date.now();
            if (immediateToggleConsumedAt && now - immediateToggleConsumedAt < playbackConfig.immediateActionConsumeMilliseconds) {
                immediateToggleConsumedAt = 0;
                return true;
            }

            return false;
        },
        consumeImmediateSeekHandled: function () {
            var now = Date.now();
            if (immediateSeekConsumedAt && now - immediateSeekConsumedAt < playbackConfig.immediateActionConsumeMilliseconds) {
                immediateSeekConsumedAt = 0;
                return true;
            }

            return false;
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
        startAudio: startAudioElement,
        pauseAudio: function (element) {
            if (!element) return;
            element.pause();
        },
        seekAudio: function (element, seconds) {
            if (!element) return;
            var target = Math.max(0, seconds || 0);
            try {
                element.currentTime = target;
            } catch (error) {
                var applyWhenReady = function () {
                    try {
                        element.currentTime = target;
                    } catch (innerError) {
                        console.debug("Audio seek was rejected.", innerError);
                    }
                };
                element.addEventListener('loadedmetadata', applyWhenReady, { once: true });
            }
        },
        setVolume: function (element, volume) {
            if (!element) return;
            var next = Math.max(0, Math.min(1, volume || 0));
            element.volume = next;
        },
        setMuted: function (element, muted) {
            if (!element) return;
            element.muted = !!muted;
        },
        setPlaybackRate: function (element, rate) {
            if (!element) return;
            var next = Math.max(0.5, Math.min(32, rate || 1));
            try {
                element.playbackRate = next;
            } catch (error) {
                console.debug("Audio playback rate was rejected.", error);
            }
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
