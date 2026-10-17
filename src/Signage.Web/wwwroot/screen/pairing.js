// The unpaired screen (PLAYER_SPEC §8): a large code, refreshed before it expires, polled every 3 s.
import { config, ms } from './config.js';
import { startPairing, pollPairing } from './api.js';
import { t } from './strings.js';

const sleep = (duration) => new Promise((resolve) => setTimeout(resolve, duration));

function render(stage, code) {
    stage.replaceChildren();
    const wrap = document.createElement('section');
    wrap.className = 'pairing';
    wrap.innerHTML = `
        <p class="product"></p>
        <h1></h1>
        <p class="code" data-testid="pairing-code"></p>
        <p class="instruction"></p>
        <p class="server"><span class="label"></span> <span class="url"></span></p>`;
    wrap.querySelector('.product').textContent = t('productName');
    wrap.querySelector('h1').textContent = t('pairTitle');
    wrap.querySelector('.code').textContent = code ?? '';
    wrap.querySelector('.instruction').textContent = t('pairInstruction');
    wrap.querySelector('.label').textContent = t('adminAddress');
    wrap.querySelector('.url').textContent = `${location.origin}/admin`;
    stage.append(wrap);
}

/**
 * Shows codes until an admin claims one; resolves once the token cookie has been delivered.
 * A fresh code is shown shortly before the current one expires, but the previous code keeps being polled until it
 * really expires: an admin who typed it in its last minute still pairs the device.
 * Deadlines come from the server's "expires in" and run on the monotonic clock, so a wrong device clock can't break this.
 */
export async function pairDevice(stage) {
    document.body.dataset.mode = 'pairing';
    render(stage, '');
    const open = []; // codes that can still be claimed: { pairingId, deviceSecret, expiresAt (performance.now ms) }
    let refreshAt = 0;
    for (;;) {
        const now = performance.now();
        if (now >= refreshAt) {
            try {
                const request = await startPairing();
                const lifetimeMs = request.expiresInSeconds * 1000;
                open.push({ pairingId: request.pairingId, deviceSecret: request.deviceSecret, expiresAt: now + lifetimeMs });
                refreshAt = now + Math.max(lifetimeMs - config.pairingRefreshBeforeExpiryMs, lifetimeMs / 2);
                render(stage, request.code);
            } catch {
                refreshAt = now + ms(config.retryAfterErrorMs); // offline or rate-limited: keep showing what we have
            }
        }

        await sleep(ms(config.pairingPollMs));
        for (const request of [...open]) {
            let result;
            try {
                result = await pollPairing(request.pairingId, request.deviceSecret);
            } catch {
                continue; // network hiccup: try again next round
            }
            if (result.status === 'paired') {
                return result.screen;
            }
            if (result.status === 'expired' || performance.now() >= request.expiresAt) {
                open.splice(open.indexOf(request), 1);
            }
        }
    }
}
