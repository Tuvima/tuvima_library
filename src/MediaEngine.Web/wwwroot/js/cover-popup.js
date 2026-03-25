// Cover hover popup — renders on document.body to escape backdrop-filter containment.
window.CoverPopup = {
    _el: null,

    show(imageUrl, clientX, clientY) {
        this.hide();

        const el = document.createElement('div');
        el.className = 'cover-popup-portal';
        el.innerHTML = `<img src="${imageUrl}" alt="" />`;
        el.style.cssText = `
            position: fixed;
            z-index: 99999;
            padding: 4px;
            background: rgba(10, 8, 20, 0.97);
            border: 1px solid rgba(255, 255, 255, 0.12);
            border-radius: 8px;
            box-shadow: 0 8px 32px rgba(0, 0, 0, 0.6);
            pointer-events: none;
            max-width: 420px;
            max-height: 85vh;
        `;

        const img = el.querySelector('img');
        img.style.cssText = 'max-width: 400px; max-height: 80vh; width: auto; height: auto; border-radius: 6px; display: block;';

        document.body.appendChild(el);
        this._el = el;

        // Position after image loads (need dimensions for clamping)
        const position = () => {
            const rect = el.getBoundingClientRect();
            const vw = window.innerWidth;
            const vh = window.innerHeight;

            // Default: to the left of cursor, vertically centered
            let left = clientX - rect.width - 16;
            let top = clientY - rect.height / 2;

            // Clamp to viewport
            if (left < 8) left = clientX + 16; // flip to right if no room
            if (top < 8) top = 8;
            if (top + rect.height > vh - 8) top = vh - rect.height - 8;
            if (left + rect.width > vw - 8) left = vw - rect.width - 8;

            el.style.left = left + 'px';
            el.style.top = top + 'px';
        };

        img.onload = position;
        // Also position immediately in case image is cached
        requestAnimationFrame(position);
    },

    hide() {
        if (this._el) {
            this._el.remove();
            this._el = null;
        }
    }
};
