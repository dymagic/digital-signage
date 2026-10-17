// Html slides (R-05, PLAYER_SPEC §5): a sandboxed iframe with scripts but never same-origin, so the editor's HTML
// can't reach the player, its cookies or the admin.
import { config, ms } from '../config.js';

// Must stay identical to HtmlSlideDocument.BaseStyle (C#): a unit test compares them, so the admin preview matches.
export const BASE_STYLE =
    "html,body{margin:0;padding:0;width:100%;height:100%;overflow:hidden;background:#000;color:#fff;font-family:system-ui,-apple-system,'Segoe UI',Roboto,sans-serif}";

export function htmlDocument(html) {
    return `<!DOCTYPE html><meta charset="utf-8"><style>${BASE_STYLE}</style>${html ?? ''}`;
}

export function createHtml(slide) {
    const frame = document.createElement('iframe');
    frame.setAttribute('sandbox', 'allow-scripts');
    frame.setAttribute('tabindex', '-1');
    frame.setAttribute('title', '');
    const ready = new Promise((resolve) => {
        frame.addEventListener('load', () => resolve(true), { once: true });
        setTimeout(() => resolve(true), ms(config.iframeLoadTimeoutMs));
    });
    frame.srcdoc = htmlDocument(slide.html);
    return { element: frame, ready, release() { frame.removeAttribute('srcdoc'); frame.src = 'about:blank'; } };
}
