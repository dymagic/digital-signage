// Keeps the manifest fresh (PLAYER_SPEC §9, R-25): server-sent events say when to fetch it, and a poll every 60 s with
// If-None-Match is the fallback (an unchanged manifest costs a 304). Errors never interrupt playback.
import { config, ms } from './config.js';
import { Unauthorized } from './api.js';

/**
 * Fetches `url` now and whenever told to, at least every 60 s, until stopped. Calls onManifest(manifest) for every new
 * version, onUnauthorized() on 401/403 (then stops), onNotFound() on 404 if given (then stops; otherwise 404 counts as
 * an error and is retried), and onOnline(boolean) when reachability changes. With `eventsUrl`, an EventSource on it
 * triggers a fetch on every `manifest-changed` event and on every (re)connect, since events may have been missed;
 * onEvents('open' | 'closed') reports the stream's state, and onIdentify(seconds, withBorder) handles identification. An event that arrives during a fetch causes another fetch.
 */
export function pollManifest(url, { eventsUrl, onManifest, onUnauthorized, onNotFound, onOnline, onEvents, onIdentify }) {
    let etag = null;
    let stopped = false;
    let online = null;
    let wake = null;
    let again = false; // a fetch was asked for while one was under way: do another right after
    let events = null;
    const setOnline = (value) => {
        if (value !== online) {
            online = value;
            onOnline?.(value);
        }
    };
    const fetchNow = () => {
        if (wake) {
            wake();
        } else {
            again = true; // the fetch under way may have read the database before this change
        }
    };
    const pause = (duration) => new Promise((resolve) => {
        const timer = setTimeout(done, duration);
        function done() {
            clearTimeout(timer);
            wake = null;
            resolve();
        }
        wake = done;
    });
    const stop = () => {
        stopped = true;
        events?.close();
        events = null;
        wake?.();
    };

    const connectEvents = () => {
        if (!eventsUrl || stopped) {
            return;
        }
        events = new EventSource(eventsUrl);
        events.addEventListener('open', () => {
            onEvents?.('open');
            fetchNow();
        });
        events.addEventListener('manifest-changed', fetchNow);
        // R-31, R-32: identification carries its duration.
        const identify = (withBorder) => (event) => {
            let seconds = 30;
            try {
                seconds = Number(JSON.parse(event.data).seconds) || seconds;
            } catch {
                // keep the default
            }
            onIdentify?.(seconds, withBorder);
        };
        events.addEventListener('identify', identify(true));
        events.addEventListener('identify-all', identify(false));
        events.addEventListener('error', () => {
            onEvents?.('closed');
            // EventSource reconnects by itself after a network error, but gives up after a non-200 answer (a proxy's
            // 502 during a restart, or a 401 that the manifest fetch will notice). Start a new one after a pause.
            if (events?.readyState === EventSource.CLOSED) {
                events = null;
                setTimeout(connectEvents, ms(config.retryAfterErrorMs));
            }
        });
    };

    const loop = async () => {
        while (!stopped) {
            try {
                const response = await fetch(url, { cache: 'no-store', headers: etag ? { 'If-None-Match': etag } : {} });
                if (response.status === 401 || response.status === 403) {
                    throw new Unauthorized();
                }
                if (response.status === 404 && onNotFound) {
                    stop();
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
                if (again) {
                    again = false;
                    continue;
                }
                await pause(ms(config.manifestPollMs));
            } catch (error) {
                if (error instanceof Unauthorized) {
                    stop();
                    onUnauthorized?.();
                    return;
                }
                setOnline(false);
                await pause(ms(config.retryAfterErrorMs));
            }
        }
    };
    connectEvents();
    loop();
    return { stop, fetchNow };
}
