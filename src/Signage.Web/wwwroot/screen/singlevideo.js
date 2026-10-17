// Single video mode (R-14, PLAYER_SPEC §6): one looping, muted <video> filling the screen, no carousel, no progress
// bar. Starting, ending or changing the URL cross-fades (300 ms). A video that fails (error, refused play(), or no
// progress for 8 s) is tried again after a calm pause with a fresh element; the last good picture stays until the new
// one plays. Every layer ever created is tracked, so replaced and failed ones are always released (ADR-0008).
import { config, ms } from './config.js';
import { reportProblem } from './problems.js';

export class SingleVideo {
    /** @param {HTMLElement} stage */
    constructor(stage) {
        this.stage = stage;
        this.entries = new Set(); // every layer still in the DOM: { url, layer, video, failedAt, watchdog }
        this.current = null; // the newest entry
        this.created = 0;
    }

    get active() {
        return this.current !== null;
    }

    /** True once the current video plays and has faded in. */
    get visible() {
        return this.current?.layer.classList.contains('visible') ?? false;
    }

    /** True when the current video failed and nothing of this mode is on screen. */
    get failed() {
        return this.current?.failedAt != null && ![...this.entries].some((e) => e.layer.classList.contains('visible'));
    }

    /** Shows `url` (cross-fading from what was there); does nothing while it plays or waits for its retry. */
    show(url) {
        const current = this.current;
        if (current?.url === url && (current.failedAt === null || performance.now() - current.failedAt < ms(config.nothingShowableRetryMs))) {
            return;
        }

        // Layers that never made it on screen have nothing to fade from: release them now.
        for (const entry of [...this.entries]) {
            if (!entry.layer.classList.contains('visible')) {
                this.release(entry);
            }
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
        const entry = { url, layer, video, failedAt: null, watchdog: 0, seq: ++this.created };
        this.entries.add(entry);
        this.current = entry;

        const fail = (reason) => {
            if (entry.failedAt === null && this.entries.has(entry)) {
                entry.failedAt = performance.now();
                clearInterval(entry.watchdog);
                reportProblem('single-video', null, `${reason}: ${url}`);
            }
        };
        video.addEventListener('error', () => fail('error'));
        video.addEventListener('playing', () => {
            layer.classList.add('visible');
            // Everything this one replaces (older layers) goes once the fade is done.
            setTimeout(() => {
                for (const other of [...this.entries]) {
                    if (other.seq < entry.seq) {
                        this.release(other);
                    }
                }
            }, ms(config.singleVideoFadeMs));
        }, { once: true });
        // No progress for 8 s counts as a failure: from the start (a server that accepts the connection but never sends
        // data) and while playing (a stream that hangs).
        let lastTime = -1;
        let lastProgressAt = performance.now();
        entry.watchdog = setInterval(() => {
            if (video.currentTime !== lastTime) {
                lastTime = video.currentTime;
                lastProgressAt = performance.now();
            } else if (performance.now() - lastProgressAt > config.videoStallMs) {
                fail(lastTime <= 0 ? 'no data' : 'stalled');
            }
        }, 500);
        video.src = url;
        this.stage.append(layer);
        video.play().catch((error) => fail(`play() failed: ${error?.name ?? error}`));
    }

    /** Fades out and releases every layer of this mode (the carousel shows underneath). */
    hide() {
        if (this.entries.size === 0) {
            return;
        }
        this.current = null;
        for (const entry of [...this.entries]) {
            entry.layer.classList.remove('visible');
            setTimeout(() => this.release(entry), ms(config.singleVideoFadeMs));
        }
    }

    release(entry) {
        if (!this.entries.delete(entry)) {
            return;
        }
        clearInterval(entry.watchdog);
        entry.video.pause();
        entry.video.removeAttribute('src');
        entry.video.load();
        entry.layer.remove();
        if (this.current === entry) {
            this.current = null;
        }
    }
}
