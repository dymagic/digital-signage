// WebPage slides (R-06, PLAYER_SPEC §5). Same-origin is allowed here because the page is another origin than the
// player; no top navigation or popups. Framing can be refused by the site and can't be detected reliably, so after
// the load timeout the slide is shown anyway.
import { config, ms } from '../config.js';

export function createWebPage(slide) {
    const frame = document.createElement('iframe');
    frame.setAttribute('sandbox', 'allow-scripts allow-same-origin allow-forms');
    frame.setAttribute('referrerpolicy', 'no-referrer');
    frame.setAttribute('tabindex', '-1');
    frame.setAttribute('title', '');
    const ready = new Promise((resolve) => {
        frame.addEventListener('load', () => resolve(true), { once: true });
        setTimeout(() => resolve(true), ms(config.iframeLoadTimeoutMs));
    });
    frame.src = slide.url;
    return { element: frame, ready, release() { frame.src = 'about:blank'; } };
}
