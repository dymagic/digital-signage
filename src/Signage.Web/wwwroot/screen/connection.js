// Keeps the manifest fresh (PLAYER_SPEC §9): a poll every 60 s with If-None-Match, so unchanged manifests cost a 304.
// WP-10 adds the SSE push; this poll stays as the fallback. Errors never interrupt playback.
import { config, ms } from './config.js';
import { Unauthorized } from './api.js';

const sleep = (duration) => new Promise((resolve) => setTimeout(resolve, duration));

/**
 * Polls `url` until stopped. Calls onManifest(manifest) for every new version, onUnauthorized() on 401/403
 * (then stops), onNotFound() on 404 if given (then stops; otherwise 404 counts as an error and is retried), and
 * onOnline(boolean) when reachability changes.
 */
export function pollManifest(url, { onManifest, onUnauthorized, onNotFound, onOnline }) {
    let etag = null;
    let stopped = false;
    let online = null;
    const setOnline = (value) => {
        if (value !== online) {
            online = value;
            onOnline?.(value);
        }
    };

    const loop = async () => {
        while (!stopped) {
            try {
                const response = await fetch(url, { cache: 'no-store', headers: etag ? { 'If-None-Match': etag } : {} });
                if (response.status === 401 || response.status === 403) {
                    throw new Unauthorized();
                }
                if (response.status === 404 && onNotFound) {
                    stopped = true;
                    onNotFound();
                    return;
                }
                if (response.ok) {
                    etag = response.headers.get('ETag');
                    onManifest(await response.json());
                } else if (response.status !== 304) {
                    throw new Error(`manifest ${response.status}`);
                }
                setOnline(true);
                await sleep(ms(config.manifestPollMs));
            } catch (error) {
                if (error instanceof Unauthorized) {
                    stopped = true;
                    onUnauthorized?.();
                    return;
                }
                setOnline(false);
                await sleep(ms(config.retryAfterErrorMs));
            }
        }
    };
    loop();
    return { stop() { stopped = true; } };
}
