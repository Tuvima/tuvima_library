// lazy-load.js — Tuvima Library
// Generic on-demand <script> loader for Blazor pages that need a heavy
// third-party bundle (e.g. Cytoscape.js) only on their own route, instead of
// paying the download cost on every page via a global <script> tag in App.razor.
//
// Usage from a Razor component (typically in OnAfterRenderAsync):
//   await JS.InvokeVoidAsync("lazyLoad.loadScript", "lib/cytoscape/cytoscape.min.js");
//
// Classic <script> elements created via document.createElement default to
// async=true, so two dynamically-inserted scripts are NOT guaranteed to run in
// document order. Setting script.async = false restores that ordering
// guarantee (per the HTML spec, non-async dynamically-inserted scripts still
// execute in insertion order), which matters for UMD bundles (like
// Cytoscape.js) that attach themselves to `window` as a side effect rather
// than via an ES module export.
window.lazyLoad = window.lazyLoad || (function () {
    var pending = {};

    function loadScript(src) {
        if (pending[src]) {
            return pending[src];
        }

        var existing = document.querySelector('script[src="' + src + '"]');
        if (existing && existing.dataset.lazyLoaded === 'true') {
            pending[src] = Promise.resolve();
            return pending[src];
        }

        pending[src] = new Promise(function (resolve, reject) {
            var script = existing || document.createElement('script');
            script.src = src;
            script.async = false;
            script.dataset.lazyLoaded = 'true';
            script.addEventListener('load', function () { resolve(); });
            script.addEventListener('error', function () {
                delete pending[src];
                reject(new Error('lazyLoad: failed to load script "' + src + '"'));
            });
            if (!existing) {
                document.head.appendChild(script);
            }
        });

        return pending[src];
    }

    return { loadScript: loadScript };
})();
