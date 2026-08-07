import { writable } from 'svelte/store';

const DEFAULT_ZOOM = 100;
export const MIN_ZOOM = 50;
export const MAX_ZOOM = 150;
export const STEP_ZOOM = 10;

const storedZoom = typeof localStorage !== 'undefined' ? localStorage.getItem('page_zoom') : null;
const initialZoom = storedZoom ? Math.min(MAX_ZOOM, Math.max(MIN_ZOOM, parseInt(storedZoom, 10) || DEFAULT_ZOOM)) : DEFAULT_ZOOM;

function createZoomStore() {
    const { subscribe, set, update } = writable<number>(initialZoom);

    return {
        subscribe,
        zoomIn: () => update(z => Math.min(MAX_ZOOM, z + STEP_ZOOM)),
        zoomOut: () => update(z => Math.max(MIN_ZOOM, z - STEP_ZOOM)),
        reset: () => set(DEFAULT_ZOOM),
        setZoom: (val: number) => set(Math.min(MAX_ZOOM, Math.max(MIN_ZOOM, val)))
    };
}

export const zoomStore = createZoomStore();

zoomStore.subscribe((val) => {
    if (typeof localStorage !== 'undefined') {
        localStorage.setItem('page_zoom', val.toString());
    }
    if (typeof document !== 'undefined') {
        const scale = val / 100;
        document.documentElement.style.setProperty('--page-zoom', scale.toString());
        document.documentElement.style.zoom = `${val}%`;
    }
});
