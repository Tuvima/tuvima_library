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