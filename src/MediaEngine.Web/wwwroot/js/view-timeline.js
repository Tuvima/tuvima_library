const observers = new WeakMap();

export function observeTimeline(sentinel, dotnet) {
  disconnectTimeline(sentinel);
  const root = sentinel.closest('.view-page') || document;
  const loadObserver = new IntersectionObserver(entries => {
    if (entries.some(entry => entry.isIntersecting)) dotnet.invokeMethodAsync('LoadMoreFromObserverAsync');
  }, { rootMargin: '700px 0px' });
  loadObserver.observe(sentinel);

  const monthObserver = new IntersectionObserver(entries => {
    const visible = entries.filter(entry => entry.isIntersecting)
      .sort((a, b) => Math.abs(a.boundingClientRect.top) - Math.abs(b.boundingClientRect.top));
    const current = visible[0];
    if (current) dotnet.invokeMethodAsync('SetActiveTimelinePeriod', Number(current.target.dataset.year), Number(current.target.dataset.month));
  }, { rootMargin: '-15% 0px -70% 0px', threshold: [0, .01] });
  root.querySelectorAll('.view-month[data-year][data-month]').forEach(element => monthObserver.observe(element));
  observers.set(sentinel, { loadObserver, monthObserver });
}

export function disconnectTimeline(sentinel) {
  const value = observers.get(sentinel);
  if (!value) return;
  value.loadObserver.disconnect();
  value.monthObserver.disconnect();
  observers.delete(sentinel);
}

export function focusFirstPeriod() {
  const heading = document.querySelector('.view-timeline .view-month > h2');
  if (!heading) return;
  heading.scrollIntoView({ block: 'start', behavior: matchMedia('(prefers-reduced-motion: reduce)').matches ? 'auto' : 'smooth' });
  heading.focus({ preventScroll: true });
}
