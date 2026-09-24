import * as maplibregl from '../vendor/maplibre/maplibre-gl.mjs';

maplibregl.setWorkerUrl('/vendor/maplibre/maplibre-gl-worker.mjs');

const states = new WeakMap();
const detailedStyleUrl = 'https://tiles.openfreemap.org/styles/dark';

async function preferredStyle() {
  try {
    const controller = new AbortController();
    const timeout = window.setTimeout(() => controller.abort(), 3500);
    const response = await fetch(detailedStyleUrl, {
      cache: 'force-cache',
      mode: 'cors',
      signal: controller.signal
    });
    window.clearTimeout(timeout);
    if (!response.ok) throw new Error(`Detailed map style returned ${response.status}`);
    return {
      style: detailedStyleUrl,
      attribution: '© OpenFreeMap · © OpenStreetMap contributors',
      detailed: true
    };
  } catch {
    return {
      style: baseStyle(),
      attribution: 'Natural Earth · Tuvima Atlas',
      detailed: false
    };
  }
}

function baseStyle() {
  return {
    version: 8,
    sources: {
      world: { type: 'geojson', data: '/maps/world-countries.geojson' }
    },
    layers: [
      { id: 'atlas-background', type: 'background', paint: { 'background-color': '#030712' } },
      { id: 'atlas-countries', type: 'fill', source: 'world', paint: {
        'fill-color': ['case', ['boolean', ['feature-state', 'active'], false], '#20253a', '#111827'],
        'fill-opacity': 0.92
      } },
      { id: 'atlas-borders', type: 'line', source: 'world', paint: {
        'line-color': '#34405a', 'line-width': 0.65, 'line-opacity': 0.72
      } }
    ]
  };
}

function normalizedHotspot(value) {
  return {
    key: value.key ?? value.Key,
    name: value.name ?? value.Name ?? 'Location',
    latitude: Number(value.latitude ?? value.Latitude),
    longitude: Number(value.longitude ?? value.Longitude),
    assetCount: Number(value.assetCount ?? value.AssetCount ?? 0),
    imageCount: Number(value.imageCount ?? value.ImageCount ?? 0),
    videoCount: Number(value.videoCount ?? value.VideoCount ?? 0),
    earliestAt: value.earliestAt ?? value.EarliestAt ?? null,
    latestAt: value.latestAt ?? value.LatestAt ?? null,
    thumbnailUrl: value.thumbnailUrl ?? value.ThumbnailUrl ?? null
  };
}

function clearMarkers(state) {
  for (const marker of state.markers) marker.remove();
  state.markers = [];
}

function renderHotspots(state) {
  if (state.mode !== 'atlas' || !state.map || !state.map.isStyleLoaded()) return;
  clearMarkers(state);
  const zoom = state.map.getZoom();
  const cell = zoom < 2 ? 92 : zoom < 4 ? 78 : zoom < 7 ? 66 : 52;
  const groups = new Map();
  for (const hotspot of state.hotspots) {
    if (!Number.isFinite(hotspot.latitude) || !Number.isFinite(hotspot.longitude)) continue;
    const point = state.map.project([hotspot.longitude, hotspot.latitude]);
    const key = `${Math.floor(point.x / cell)}:${Math.floor(point.y / cell)}`;
    const group = groups.get(key) ?? { items: [], count: 0, images: 0, videos: 0, representative: null };
    group.items.push(hotspot);
    group.count += hotspot.assetCount;
    group.images += hotspot.imageCount;
    group.videos += hotspot.videoCount;
    if (hotspot.thumbnailUrl && (!group.representative || hotspot.assetCount > group.representative.assetCount))
      group.representative = hotspot;
    groups.set(key, group);
  }

  for (const group of groups.values()) {
    const latitude = group.items.reduce((sum, item) => sum + item.latitude * Math.max(1, item.assetCount), 0) / Math.max(1, group.count);
    const longitude = group.items.reduce((sum, item) => sum + item.longitude * Math.max(1, item.assetCount), 0) / Math.max(1, group.count);
    const element = document.createElement('button');
    element.type = 'button';
    element.className = `tuvima-map-hotspot${group.items.length === 1 && zoom >= 7 ? ' is-place' : ''}`;
    const count = document.createElement('span');
    count.textContent = new Intl.NumberFormat(undefined, { notation: group.count > 999 ? 'compact' : 'standard' }).format(group.count);
    element.appendChild(count);
    const mediaTotal = Math.max(1, group.images + group.videos);
    element.style.setProperty('--video-share', `${Math.round(group.videos / mediaTotal * 360)}deg`);
    if (group.representative?.thumbnailUrl) {
      const image = document.createElement('img');
      image.src = group.representative.thumbnailUrl;
      image.alt = '';
      element.prepend(image);
      element.classList.add('has-thumbnail');
      if (group.items.length > 1) element.classList.add('is-cluster');
    }
    const label = group.items.length === 1
      ? `${group.items[0].name}, ${group.count} media item${group.count === 1 ? '' : 's'}`
      : `${group.count} media items in ${group.items.length} nearby places`;
    element.setAttribute('aria-label', label);
    element.title = label;
    element.addEventListener('click', event => {
      event.stopPropagation();
      if (group.items.length === 1) {
        state.dotnet.invokeMethodAsync('SelectHotspot', group.items[0].key);
        state.map.easeTo({ center: [longitude, latitude], zoom: Math.max(state.map.getZoom(), 8), duration: reducedMotion() ? 0 : 650 });
        return;
      }
      const bounds = new maplibregl.LngLatBounds();
      for (const item of group.items) bounds.extend([item.longitude, item.latitude]);
      if (bounds.getNorthEast().equals(bounds.getSouthWest()))
        state.map.easeTo({ center: [longitude, latitude], zoom: Math.min(14, zoom + 2), duration: reducedMotion() ? 0 : 650 });
      else
        state.map.fitBounds(bounds, { padding: 92, maxZoom: 10, duration: reducedMotion() ? 0 : 700 });
    });
    state.markers.push(new maplibregl.Marker({ element, anchor: 'center' }).setLngLat([longitude, latitude]).addTo(state.map));
  }
}

function reducedMotion() {
  return window.matchMedia?.('(prefers-reduced-motion: reduce)').matches === true;
}

function atlasWorldCenter(hotspots) {
  if (!hotspots?.length) return [0, 18];
  const total = hotspots.reduce((sum, item) => sum + Math.max(1, item.assetCount), 0);
  return [
    hotspots.reduce((sum, item) => sum + item.longitude * Math.max(1, item.assetCount), 0) / total,
    Math.max(-55, Math.min(65, hotspots.reduce((sum, item) => sum + item.latitude * Math.max(1, item.assetCount), 0) / total))
  ];
}

function journeyGeoJson(hotspots) {
  const ordered = hotspots.filter(item => item.earliestAt).sort((left, right) => new Date(left.earliestAt) - new Date(right.earliestAt));
  const lines = [];
  let current = [];
  let previous = null;
  for (const item of ordered) {
    const at = new Date(item.earliestAt);
    if (previous && at - previous > 1000 * 60 * 60 * 24 * 21) {
      if (current.length > 1) lines.push(current);
      current = [];
    }
    current.push([item.longitude, item.latitude]);
    previous = at;
  }
  if (current.length > 1) lines.push(current);
  return { type: 'Feature', properties: {}, geometry: { type: 'MultiLineString', coordinates: lines } };
}

function updateJourney(state, enabled) {
  state.journeyEnabled = enabled === true;
  if (!state.map?.loaded()) return;
  const source = state.map.getSource('atlas-journey');
  if (source) source.setData(journeyGeoJson(state.journeyEnabled ? state.hotspots : []));
}

export async function initialize(container, dotnet, options) {
  if (!container) return;
  await dispose(container);
  const mode = options.mode ?? 'location';
  const state = { map: null, dotnet, mode, markers: [], hotspots: (options.hotspots ?? []).map(normalizedHotspot), locationMarker: null, resizeObserver: null, journeyEnabled: options.journeyEnabled === true };
  states.set(container, state);
  try {
    const styleSelection = await preferredStyle();
    const map = new maplibregl.Map({
      container,
      style: styleSelection.style,
      center: mode === 'atlas' ? atlasWorldCenter(state.hotspots) : [Number(options.longitude ?? 0), Number(options.latitude ?? 0)],
      zoom: mode === 'atlas' ? 0.8 : Number(options.zoom ?? 11),
      minZoom: mode === 'atlas' ? 0 : 1,
      maxZoom: 18,
      renderWorldCopies: false,
      attributionControl: false,
      dragPan: options.interactive !== false,
      scrollZoom: mode === 'atlas' && options.interactive !== false,
      doubleClickZoom: options.interactive !== false,
      touchZoomRotate: options.interactive !== false
    });
    state.map = map;
    state.resizeObserver = new ResizeObserver(() => map.resize());
    state.resizeObserver.observe(container);
    map.addControl(new maplibregl.AttributionControl({ compact: true, customAttribution: styleSelection.attribution }), 'bottom-right');
    if (options.showNavigation !== false) map.addControl(new maplibregl.NavigationControl({ showCompass: false }), 'top-left');
    map.on('load', () => {
      if (mode === 'atlas') {
        try { map.setProjection({ type: 'globe' }); } catch { }
        map.addSource('atlas-journey', { type: 'geojson', data: journeyGeoJson(state.journeyEnabled ? state.hotspots : []) });
        map.addLayer({ id: 'atlas-journey-glow', type: 'line', source: 'atlas-journey', paint: { 'line-color': '#7c3aed', 'line-width': 8, 'line-opacity': .16, 'line-blur': 6 } });
        map.addLayer({ id: 'atlas-journey', type: 'line', source: 'atlas-journey', paint: { 'line-color': '#c4b5fd', 'line-width': 2, 'line-opacity': .72, 'line-dasharray': [2, 2] } });
        renderHotspots(state);
        map.once('idle', () => renderHotspots(state));
      } else {
        const marker = new maplibregl.Marker({ color: '#9f67ff', draggable: options.editable === true })
          .setLngLat([Number(options.longitude ?? 0), Number(options.latitude ?? 0)])
          .addTo(map);
        state.locationMarker = marker;
        if (options.editable === true) {
          marker.on('dragend', () => {
            const point = marker.getLngLat();
            state.dotnet.invokeMethodAsync('CoordinateChanged', point.lat, point.lng);
          });
          map.on('click', event => {
            marker.setLngLat(event.lngLat);
            state.dotnet.invokeMethodAsync('CoordinateChanged', event.lngLat.lat, event.lngLat.lng);
          });
        }
      }
      state.dotnet.invokeMethodAsync('MapReady');
    });
    if (mode === 'atlas') {
      map.on('moveend', () => {
        renderHotspots(state);
        const center = map.getCenter();
        state.dotnet.invokeMethodAsync('CameraChanged', center.lat, center.lng, map.getZoom());
      });
    }
    map.on('error', event => {
      if (!map.isStyleLoaded() && event?.error?.message)
        state.dotnet.invokeMethodAsync('MapFailed', event.error.message);
    });
  } catch (error) {
    state.dotnet.invokeMethodAsync('MapFailed', error?.message ?? 'The map could not be initialized.');
  }
}

export function updateHotspots(container, hotspots) {
  const state = states.get(container);
  if (!state) return;
  state.hotspots = (hotspots ?? []).map(normalizedHotspot);
  updateJourney(state, state.journeyEnabled);
  renderHotspots(state);
}

export function setJourney(container, enabled) {
  const state = states.get(container);
  if (state) updateJourney(state, enabled);
}

export function updateLocation(container, latitude, longitude) {
  const state = states.get(container);
  if (!state?.map || !Number.isFinite(latitude) || !Number.isFinite(longitude)) return;
  state.locationMarker?.setLngLat([longitude, latitude]);
  state.map.easeTo({ center: [longitude, latitude], duration: reducedMotion() ? 0 : 350 });
}

export function resetWorld(container) {
  const state = states.get(container);
  state?.map?.easeTo({ center: atlasWorldCenter(state.hotspots), zoom: 0.8, duration: reducedMotion() ? 0 : 700 });
}

export function fitHotspots(container) {
  const state = states.get(container);
  if (!state?.map || state.hotspots.length === 0) return;
  const bounds = new maplibregl.LngLatBounds();
  for (const item of state.hotspots) bounds.extend([item.longitude, item.latitude]);
  state.map.fitBounds(bounds, { padding: 110, maxZoom: 9, duration: reducedMotion() ? 0 : 700 });
}

export function resize(container) {
  states.get(container)?.map?.resize();
}

export async function dispose(container) {
  const state = states.get(container);
  if (!state) return;
  clearMarkers(state);
  state.resizeObserver?.disconnect();
  state.locationMarker?.remove();
  state.map?.remove();
  states.delete(container);
}
