// Player entry point (/screen). Boot order (PLAYER_SPEC §9): take a pre-configured token from the address, start
// playing the cached manifest at once, then keep the manifest fresh. A 401 sends the device to pairing.
// /screen?preview={screenId} plays a screen's manifest in an admin's browser (R-13): admin cookie, nothing stored.
import { config, ms } from './config.js';
import { exchangeToken } from './api.js';
import { Carousel } from './carousel.js';
import { pollManifest } from './connection.js';
import { pairDevice } from './pairing.js';
import { startDailyReload } from './reload.js';
import { loadManifest, saveManifest, clearManifest } from './storage.js';
import { t } from './strings.js';

const params = new URLSearchParams(location.search);
const previewId = params.get('preview');
const stage = document.getElementById('stage');
const sleep = (duration) => new Promise((resolve) => setTimeout(resolve, duration));

function applyLook() {
    const root = document.documentElement.style;
    root.setProperty('--transition-ms', `${ms(config.transitionMs)}ms`);
    root.setProperty('--transition-easing', config.transitionEasing);
    root.setProperty('--transition-shadow', config.transitionShadow);
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
    });
}

async function runScreen() {
    await takeTokenFromUrl();
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

    pollManifest('/api/player/manifest', {
        onManifest(fresh) {
            manifest = fresh;
            saveManifest(fresh);
            carousel.setManifest(fresh);
            start();
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
