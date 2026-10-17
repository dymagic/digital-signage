// Single video mode (R-14, PLAYER_SPEC §6): one looping, muted <video> filling the screen, no carousel, no progress
// bar. Starting, ending or changing the URL cross-fades (300 ms). Covered or replaced videos are released like video
// slides (ADR-0008). A video that fails is tried again after a calm pause.
import { config, ms } from './config.js';

function release(entry) {
    entry.video.pause();
    entry.video.removeAttribute('src');
    entry.video.load();
    entry.layer.remove();
}

export class SingleVideo {
    /** @param {HTMLElement} stage */
    constructor(stage) {
        this.stage = stage;
        this.current = null; // { url, layer, video, failedAt }
    }

    get active() {
        return this.current !== null;
    }

    /** True once the current video plays and has faded in. */
    get visible() {
        return this.current?.layer.classList.contains('visible') ?? false;
    }

    /** Shows `url` (cross-fading from what was there); does nothing if it already plays. */
    show(url) {
        const now = performance.now();
        if (this.current?.url === url && (this.current.failedAt === null || now - this.current.failedAt < ms(config.nothingShowableRetryMs))) {
            return;
        }

        let previous = this.current;
        if (previous && !previous.layer.classList.contains('visible')) {
            release(previous); // it never got on screen (failed or still loading): nothing to fade from
            previous = null;
        }
        const layer = document.createElement('div');
        layer.className = 'single-video';
        layer.dataset.testid = 'single-video';
        const video = document.createElement('video');
        video.muted = true;
        video.defaultMuted = true;
        video.loop = true;
        video.playsInline = true;
        video.preload = 'auto';
        video.setAttribute('muted', '');
        video.setAttribute('playsinline', '');
        layer.append(video);
        const entry = { url, layer, video, failedAt: null };
        this.current = entry;

        video.addEventListener('error', () => {
            entry.failedAt = performance.now();
            console.warn(`Single video failed: ${url}`); // reported in the heartbeat from WP-10
        });
        video.addEventListener('playing', () => {
            layer.classList.add('visible');
            if (previous) {
                setTimeout(() => release(previous), ms(config.singleVideoFadeMs));
            }
        }, { once: true });
        video.src = url ?? '';
        this.stage.append(layer);
        video.play().catch(() => {}); // muted autoplay is allowed; an error shows up as the error event
    }

    /** Fades out and releases the video (the carousel shows underneath). */
    hide() {
        const entry = this.current;
        if (!entry) {
            return;
        }
        this.current = null;
        entry.layer.classList.remove('visible');
        setTimeout(() => release(entry), ms(config.singleVideoFadeMs));
    }
}
