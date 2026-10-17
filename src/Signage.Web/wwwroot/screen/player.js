// Player entry point. WP-06: token exchange, pairing, and a placeholder once paired; WP-08 adds the carousel.
import { config, ms } from './config.js';
import { exchangeToken, getManifest, Unauthorized } from './api.js';
import { pairDevice } from './pairing.js';
import { t } from './strings.js';

const stage = document.getElementById('stage');
const sleep = (duration) => new Promise((resolve) => setTimeout(resolve, duration));

/** R-41: /screen?token=… is exchanged for the cookie, then removed from the address bar. */
async function takeTokenFromUrl() {
    const url = new URL(location.href);
    const token = url.searchParams.get('token');
    if (!token) {
        return;
    }
    url.searchParams.delete('token');
    history.replaceState(null, '', url.pathname + (url.search ? url.search : '') + url.hash);
    await exchangeToken(token);
}

function showPaired(manifest) {
    document.body.dataset.mode = 'player';
    document.body.dataset.screenId = manifest.screen.id;
    stage.replaceChildren();
    const wrap = document.createElement('section');
    wrap.className = 'waiting';
    const name = document.createElement('p');
    name.className = 'screen-name';
    name.dataset.testid = 'screen-name';
    name.textContent = manifest.screen.name;
    const note = document.createElement('p');
    note.className = 'note';
    note.textContent = t('waitingForContent');
    wrap.append(name, note);
    stage.append(wrap);
}

async function run() {
    await takeTokenFromUrl();
    for (;;) {
        try {
            showPaired(await getManifest());
            await sleep(ms(config.manifestPollMs));
        } catch (error) {
            if (error instanceof Unauthorized) {
                // Unknown or revoked token (R-42): the server already removed the cookie.
                await pairDevice(stage);
            } else {
                await sleep(ms(config.retryAfterErrorMs));
            }
        }
    }
}

run();
