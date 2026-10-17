// The last manifest, kept in localStorage so a screen starts playing at once and keeps going offline (R-24).
import { config } from './config.js';

export function loadManifest() {
    try {
        const raw = localStorage.getItem(config.manifestCacheKey);
        return raw ? JSON.parse(raw) : null;
    } catch {
        return null;
    }
}

export function saveManifest(manifest) {
    try {
        localStorage.setItem(config.manifestCacheKey, JSON.stringify(manifest));
    } catch {
        // Storage full or blocked: playback still works, only the offline start doesn't.
    }
}

export function clearManifest() {
    try {
        localStorage.removeItem(config.manifestCacheKey);
    } catch {
        // ignore
    }
}
