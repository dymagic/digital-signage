// Video slides (R-22, PLAYER_SPEC §4, ADR-0008). A fresh <video> per slide: muted unless the slide plays audio (R-07),
// inline, preloading, never autoplaying. play() comes only from start(), which the carousel calls once the slide is
// fully in view. `finished` resolves when the slide should give way: on `ended` (the last frame stays visible), on an
// error, on a stall without progress, or at the safety cap. release() frees the decoder and buffers.
import { config } from '../config.js';

export function createVideo(slide) {
    const video = document.createElement('video');
    video.muted = !slide.playAudio;
    video.defaultMuted = !slide.playAudio;
    video.playsInline = true;
    video.preload = 'auto';
    video.autoplay = false;
    video.setAttribute('playsinline', '');
    if (!slide.playAudio) {
        video.setAttribute('muted', '');
    }

    let finish;
    const finished = new Promise((resolve) => { finish = resolve; });
    let done = false;
    let watchdog = 0;
    let cap = 0;
    const end = (reason) => {
        if (done) {
            return;
        }
        done = true;
        clearInterval(watchdog);
        clearTimeout(cap);
        if (reason !== 'ended' && reason !== 'released') {
            console.warn(`Video slide ${slide.id} skipped: ${reason}`); // reported in the heartbeat from WP-10
        }
        finish(reason);
    };

    const ready = new Promise((resolve) => {
        video.addEventListener('canplay', () => resolve(true), { once: true });
        video.addEventListener('error', () => resolve(false), { once: true });
    });
    video.addEventListener('ended', () => end('ended'));
    video.addEventListener('error', () => end('error'));
    video.src = slide.url;

    return {
        element: video,
        ready,
        finished,

        /** Starts playback (the slide is fully in view). A stall or a missing end is guarded by timers. */
        start() {
            let lastTime = -1;
            let lastProgressAt = performance.now();
            watchdog = setInterval(() => {
                if (video.currentTime !== lastTime) {
                    lastTime = video.currentTime;
                    lastProgressAt = performance.now();
                } else if (performance.now() - lastProgressAt > config.videoStallMs) {
                    end('stalled');
                }
            }, 500);
            cap = setTimeout(() => end('safety cap'), config.videoSafetyCapMs);
            video.play().catch((error) => {
                if (error?.name === 'NotAllowedError' && !video.muted) {
                    // Sound needs a browser started with an autoplay policy (R-07, README): play muted rather than not at all.
                    console.warn(`Video slide ${slide.id}: playing muted, the browser blocks autoplay with sound`);
                    video.muted = true;
                    video.play().catch((again) => end(`play() failed: ${again?.name ?? again}`));
                } else {
                    end(`play() failed: ${error?.name ?? error}`);
                }
            });
        },

        /** Loops while it is the only slide (PLAYER_SPEC §2); `ended` then never fires. */
        setLoop(loop) {
            video.loop = loop;
            clearTimeout(cap);
            if (!loop) {
                cap = setTimeout(() => end('safety cap'), config.videoSafetyCapMs);
            }
        },

        /** Playback position 0–1, for the progress bar (R-21). */
        progress() {
            return Number.isFinite(video.duration) && video.duration > 0 ? video.currentTime / video.duration : 0;
        },

        release() {
            end('released');
            video.pause();
            video.removeAttribute('src');
            video.load();
        },
    };
}
