const observers = new WeakMap();

// Bounded box ratios protect extreme assets; their image uses contain, never stretching.
export function rows(ratios, width, target = 160, gap = 7) {
  const result = [];
  let pending = [], sum = 0;
  for (const ratio of ratios) {
    const aspect = Math.min(4, Math.max(.2, Number.isFinite(ratio) && ratio > 0 ? ratio : 1));
    pending.push(aspect); sum += aspect;
    if (sum * target + gap * (pending.length - 1) >= width) {
      const height = Math.max(1, (width - gap * (pending.length - 1)) / sum);
      result.push(pending.map(value => ({ width: value * height, height })));
      pending = []; sum = 0;
    }
  }
  if (pending.length) {
    const height = Math.min(target, Math.max(1, (width - gap * (pending.length - 1)) / sum));
    result.push(pending.map(value => ({ width: value * height, height })));
  }
  return result;
}

export function observe(anchor) {
  const root = anchor?.parentElement;
  if (!root) return;
  if (observers.has(root)) { observers.get(root).layout(); return; }
  const layout = () => {
    const width = root.clientWidth;
    if (!width) return;
    const tiles = [...root.children].filter(tile => !tile.hasAttribute('data-gallery-observer'));
    const ratios = tiles.map(tile => {
      const css = getComputedStyle(tile);
      return parseFloat(css.getPropertyValue('--view-aspect') || css.getPropertyValue('--artwork-gallery-aspect')) || 1;
    });
    const gap = 7;
    const target = root.closest('.view-density--compact') ? 128 : root.closest('.view-density--relaxed') ? 192 : 160;
    root.style.cssText = 'display:flex;flex-wrap:wrap;gap:7px;align-items:flex-start;min-width:0';
    const boxes = rows(ratios, width, width < 600 ? Math.min(target, 128) : target, gap).flat();
    tiles.forEach((tile, index) => {
      const box = boxes[index];
      tile.style.flex = `0 0 ${box.width}px`;
      tile.style.width = `${box.width}px`;
      tile.style.height = `${box.height}px`;
      tile.style.minWidth = '0'; tile.style.maxWidth = 'none'; tile.style.boxSizing = 'border-box';
      if (ratios[index] > 4 || ratios[index] < .2) {
        const img = tile.querySelector('img');
        if (img) img.style.objectFit = 'contain';
      }
    });
  };
  let previousWidth = -1;
  const observer = new ResizeObserver(() => {
    if (root.clientWidth === previousWidth) return;
    previousWidth = root.clientWidth;
    layout();
  });
  observer.observe(root); observers.set(root, { observer, layout }); layout();
}
export function disconnect(anchor) { const root = anchor?.parentElement; observers.get(root)?.observer.disconnect(); observers.delete(root); }
