let opener = null;
const states = new WeakMap();

export function initialize(root, stage) {
  opener = document.activeElement;
  root.focus({ preventScroll: true });
  const state = { scale: 1, x: 0, y: 0, dragging: false, px: 0, py: 0, pointers: new Map(), pinchDistance: 0, pinchScale: 1 };
  states.set(stage, state);
  const image = () => stage.querySelector('img');
  const render = () => { const target = image(); if (target) target.style.transform = `translate(${state.x}px,${state.y}px) scale(${state.scale})`; };
  stage.addEventListener('wheel', event => {
    if (!image()) return;
    event.preventDefault();
    state.scale = Math.max(1, Math.min(4, state.scale + (event.deltaY < 0 ? .2 : -.2)));
    if (state.scale === 1) state.x = state.y = 0;
    render();
  }, { passive: false });
  const distance = () => { const points = [...state.pointers.values()]; return points.length < 2 ? 0 : Math.hypot(points[0].x - points[1].x, points[0].y - points[1].y); };
  stage.addEventListener('pointerdown', event => {
    if (!image()) return;
    state.pointers.set(event.pointerId, { x: event.clientX, y: event.clientY });
    stage.setPointerCapture(event.pointerId);
    if (state.pointers.size === 2) { state.pinchDistance = distance(); state.pinchScale = state.scale; state.dragging = false; }
    else if (state.scale > 1) { state.dragging = true; state.px = event.clientX; state.py = event.clientY; }
  });
  stage.addEventListener('pointermove', event => {
    if (!state.pointers.has(event.pointerId)) return;
    state.pointers.set(event.pointerId, { x: event.clientX, y: event.clientY });
    if (state.pointers.size === 2 && state.pinchDistance > 0) {
      state.scale = Math.max(1, Math.min(4, state.pinchScale * distance() / state.pinchDistance));
      if (state.scale === 1) state.x = state.y = 0;
      render(); return;
    }
    if (!state.dragging) return;
    state.x += event.clientX - state.px; state.y += event.clientY - state.py; state.px = event.clientX; state.py = event.clientY; render();
  });
  const release = event => { state.pointers.delete(event.pointerId); state.dragging = false; if (state.pointers.size < 2) state.pinchDistance = 0; };
  stage.addEventListener('pointerup', release);
  stage.addEventListener('pointercancel', release);
}
export function setZoom(stage, scale) { const state = states.get(stage); if (!state) return; state.scale = scale; if (scale === 1) state.x = state.y = 0; const image = stage.querySelector('img'); if (image) image.style.transform = `translate(${state.x}px,${state.y}px) scale(${scale})`; }
export function fit(stage) { setZoom(stage, 1); }
export function fullscreen(root) { if (!document.fullscreenElement) return root.requestFullscreen?.(); return document.exitFullscreen?.(); }
export function toggleVideo(video) { if (!video) return; if (video.paused) video.play(); else video.pause(); }
export function restoreFocus() { opener?.focus?.({ preventScroll: true }); opener = null; }
export function dispose(stage) { states.delete(stage); }
