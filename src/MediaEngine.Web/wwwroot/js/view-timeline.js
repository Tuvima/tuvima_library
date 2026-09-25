const observers = new WeakMap();

export function observeTimeline(anchor, dotnet, canLoadMore = false) {
  disconnectTimeline(anchor);

  const root = anchor.closest('.media-section-shell__content');
  const timeline = anchor.parentElement?.querySelector('.view-timeline');
  if (!root || !timeline) return;

  let loadObserver = null;
  if (canLoadMore) {
    loadObserver = new IntersectionObserver(entries => {
      if (entries.some(entry => entry.isIntersecting)) {
        dotnet.invokeMethodAsync('LoadMoreFromObserverAsync');
      }
    }, { root, rootMargin: '700px 0px' });
    loadObserver.observe(anchor);
  }

  let frame = 0;
  let lastPeriod = '';
  const updateActivePeriod = () => {
    frame = 0;
    const sections = [...timeline.querySelectorAll('.view-month[data-year][data-month]')];
    if (!sections.length) return;

    const rootRect = root.getBoundingClientRect();
    const activationLine = rootRect.top + Math.min(180, rootRect.height * .22);
    let current = sections[0];
    for (const section of sections) {
      const rect = section.getBoundingClientRect();
      if (rect.top <= activationLine) current = section;
      else break;
    }

    const period = `${current.dataset.year}-${current.dataset.month}`;
    if (period === lastPeriod) return;
    lastPeriod = period;
    dotnet.invokeMethodAsync(
      'SetActiveTimelinePeriod',
      Number(current.dataset.year),
      Number(current.dataset.month));
  };

  const scheduleUpdate = () => {
    if (frame) return;
    frame = requestAnimationFrame(updateActivePeriod);
  };

  root.addEventListener('scroll', scheduleUpdate, { passive: true });
  const resizeObserver = new ResizeObserver(scheduleUpdate);
  resizeObserver.observe(timeline);
  updateActivePeriod();

  observers.set(anchor, {
    loadObserver,
    resizeObserver,
    root,
    scheduleUpdate,
    cancelFrame: () => frame && cancelAnimationFrame(frame)
  });
}

export function disconnectTimeline(anchor) {
  const value = observers.get(anchor);
  if (!value) return;
  value.loadObserver?.disconnect();
  value.resizeObserver?.disconnect();
  value.root.removeEventListener('scroll', value.scheduleUpdate);
  value.cancelFrame();
  observers.delete(anchor);
}

export function focusFirstPeriod() {
  const heading = document.querySelector('.view-timeline .view-month > h2');
  if (!heading) return;
  heading.scrollIntoView({
    block: 'start',
    behavior: matchMedia('(prefers-reduced-motion: reduce)').matches ? 'auto' : 'smooth'
  });
  heading.focus({ preventScroll: true });
}
