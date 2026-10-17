// Player entry point (/screen). Boot order (PLAYER_SPEC §9): start playing the cached manifest at once (also when the
// server is down), exchange a pre-configured token from the address, then keep the manifest fresh. A 401 sends the device to pairing.
// /screen?preview={screenId} plays a screen's manifest in an admin's browser (R-13): admin cookie, nothing stored.
import { config, ms } from './config.js';
import { exchangeToken } from './api.js';
import { Carousel } from './carousel.js';
import { pollManifest } from './connection.js';
import { pairDevice } from './pairing.js';
import { startDailyReload, reloadSoon } from './reload.js';
import { startHeartbeat } from './heartbeat.js';
import { loadManifest, saveManifest, clearManifest } from './storage.js';
import { t } from './strings.js';

const params = new URLSearchParams(location.search);
const previewId = params.get('preview');
const stage = document.getElementById('stage');
const sleep = (duration) => new Promise((resolve) => setTimeout(resolve, duration));
// The app version this page was served with (/screen stamps it); a manifest from a newer server makes it reload (R-26).
const loadedVersion = document.querySelector('meta[name="signage-version"]')?.content || null;
const reloadedForKey = 'signage.reloadedFor';

function applyLook() {
    const root = document.documentElement.style;
    root.setProperty('--transition-ms', `${ms(config.transitionMs)}ms`);
    root.setProperty('--transition-easing', config.transitionEasing);
    root.setProperty('--transition-shadow', config.transitionShadow);
    root.setProperty('--single-video-fade-ms', `${ms(config.singleVideoFadeMs)}ms`);
}

/** Hides the mouse pointer after a few seconds without movement (PLAYER_SPEC §1). */
function hideIdleCursor() {
    let timer;
    const show = () => {
        document.body.classList.remove('cursor-hidden');
        clearTimeout(timer);
        timer = setTimeout(() => document.body.classList.add('cursor-hidden'), config.cursorHideMs);
    };
    document.addEventListener('mousemove', show);
    show();
}

/**
 * R-41: /screen?token=… is exchanged for the cookie. Until the server gives a definite answer (a kiosk often
 * boots before the network or the server is up) the token stays in the address bar and the exchange is retried,
 * so a reload or a later attempt still has it. Then it is removed from the address bar.
 */
async function takeTokenFromUrl() {
    const url = new URL(location.href);
    const token = url.searchParams.get('token');
    if (!token) {
        return;
    }
    for (;;) {
        try {
            await exchangeToken(token); // accepted or unknown: either way we're done with it
            break;
        } catch {
            await sleep(ms(config.retryAfterErrorMs));
        }
    }
    url.searchParams.delete('token');
    history.replaceState(null, '', url.pathname + url.search + url.hash);
}

function showMessage(text) {
    stage.replaceChildren();
    const note = document.createElement('p');
    note.className = 'message';
    note.textContent = text;
    stage.append(note);
}

function runPreview() {
    document.body.dataset.preview = 'true';
    const carousel = new Carousel(stage);
    let started = false;
    pollManifest(`/api/player/preview/${encodeURIComponent(previewId)}/manifest`, {
        onManifest(manifest) {
            carousel.setManifest(manifest);
            if (!started) {
                started = true;
                carousel.run();
            }
        },
        onUnauthorized() {
            carousel.stop();
            showMessage(t('previewSignIn'));
        },
        onNotFound() {
            carousel.stop();
            showMessage(t('previewNotFound'));
        },
    });
}

/**
 * R-26: the server runs another version than this page: reload at the next transition to load the new player. Once per
 * version: if the page still reports the old version afterwards (a stale cache in between), don't reload in a loop.
 */
function reloadOnNewVersion(serverVersion, carousel) {
    if (!loadedVersion || !serverVersion || serverVersion === loadedVersion) {
        return;
    }
    let reloadedFor = null;
    try {
        reloadedFor = sessionStorage.getItem(reloadedForKey);
    } catch {
        // storage unavailable: still reload once
    }
    if (reloadedFor === serverVersion) {
        return;
    }
    reloadSoon(carousel, () => {
        try {
            sessionStorage.setItem(reloadedForKey, serverVersion);
        } catch {
            // ignore
        }
    });
}

async function runScreen() {
    const carousel = new Carousel(stage);
    let manifest = loadManifest();
    let started = false;
    const start = () => {
        if (!started) {
            started = true;
            carousel.run();
            startDailyReload(carousel, () => manifest?.settings?.timeZone);
        }
    };

    if (manifest) {
        carousel.setManifest(manifest); // play the last known sequence straight away, also offline (R-24)
        start();
    }

    // The exchange may retry for a long time (a kiosk boots before its server); the cached slides play meanwhile (R-24).
    await takeTokenFromUrl();
    let heartbeat = false;
    pollManifest('/api/player/manifest', {
        eventsUrl: '/api/player/events',
        onManifest(fresh) {
            manifest = fresh;
            saveManifest(fresh);
            carousel.setManifest(fresh);
            start();
            if (!heartbeat) {
                heartbeat = true; // the server knows this device: report in from now on (R-33)
                startHeartbeat({ playerVersion: loadedVersion ?? fresh.appVersion, currentSlideId: () => carousel.currentSlideId });
            }
            reloadOnNewVersion(fresh.appVersion, carousel);
        },
        async onUnauthorized() {
            // Unknown or revoked token (R-42): the server already deleted the cookie.
            carousel.stop();
            clearManifest();
            await pairDevice(stage);
            location.reload();
        },
        onOnline(online) {
            document.body.dataset.online = String(online);
        },
    });
}

applyLook();
hideIdleCursor();
if (previewId) {
    runPreview();
} else {
    runScreen();
}
