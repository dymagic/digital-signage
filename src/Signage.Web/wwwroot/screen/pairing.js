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

/** Shows codes until an admin claims one; resolves once the token cookie has been delivered. */
export async function pairDevice(stage) {
    document.body.dataset.mode = 'pairing';
    render(stage, '');
    for (;;) {
        let request;
        try {
            request = await startPairing();
        } catch {
            await sleep(ms(config.retryAfterErrorMs));
            continue;
        }

        render(stage, request.code);
        // Timed from the server's "expires in", on the monotonic clock: a wrong device clock can't break pairing.
        const lifetimeMs = request.expiresInSeconds * 1000;
        const refreshAt = performance.now() + Math.max(lifetimeMs - config.pairingRefreshBeforeExpiryMs, lifetimeMs / 2);
        while (performance.now() < refreshAt) {
            await sleep(ms(config.pairingPollMs));
            let result;
            try {
                result = await pollPairing(request.pairingId, request.deviceSecret);
            } catch {
                continue; // network hiccup: keep the code on screen and try again
            }
            if (result.status === 'paired') {
                return result.screen;
            }
            if (result.status === 'expired') {
                break;
            }
        }
    }
}
