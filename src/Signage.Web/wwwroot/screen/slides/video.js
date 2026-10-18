// Video slides (R-22, PLAYER_SPEC §4, ADR-0008, ADR-0009). A fresh <video> per slide: muted unless the slide plays audio
// (R-07), inline, preloading, never autoplaying. play() comes only from start(), which the carousel calls once the
// slide is fully in view. `finished` resolves when the slide should give way: on `ended` (the last frame stays
// visible), on an error, on a stall without progress, or at the safety cap. A video that ended can replay() (it stays on
// screen for another round); one that failed is replaced by the carousel with a fresh element. release() frees the
// decoder and buffers.
import { config } from '../config.js';
import { reportProblem } from '../problems.js';

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

    let state = 'ready'; // ready → playing → ended | failed; release() → released
    let finish;
    let finished = new Promise((resolve) => { finish = resolve; });
    let watchdog = 0;
    let cap = 0;

    const stopTimers = () => {
        clearInterval(watchdog);
        clearTimeout(cap);
    };
    const armCap = () => {
        clearTimeout(cap);
        cap = setTimeout(() => end('failed', 'safety cap'), config.videoSafetyCapMs);
    };
    const end = (next, reason) => {
        if (state !== 'playing') {
            return;
        }
        state = next;
        stopTimers();
        if (next === 'failed') {
            reportProblem('video', slide.id, reason);
        }
        finish(reason);
    };

    const play = () => {
        if (state !== 'ready') {
            finished = new Promise((resolve) => { finish = resolve; });
        }
        state = 'playing';
        let lastTime = -1;
        let lastProgressAt = performance.now();
        watchdog = setInterval(() => {
            if (video.currentTime !== lastTime) {
                lastTime = video.currentTime;
                lastProgressAt = performance.now();
            } else if (performance.now() - lastProgressAt > config.videoStallMs) {
                end('failed', 'stalled');
            }
        }, 500);
        if (!video.loop) {
            armCap();
        }
        video.play().catch((error) => {
            if (error?.name === 'NotAllowedError' && !video.muted) {
                // Sound needs a browser started with an autoplay policy (R-07, ADR-0009): play muted rather than not at all.
                reportProblem('video', slide.id, 'played muted: the browser blocks autoplay with sound');
                video.muted = true;
                video.play().catch((again) => end('failed', `play() failed: ${again?.name ?? again}`));
            } else {
                end('failed', `play() failed: ${error?.name ?? error}`);
            }
        });
    };

    const ready = new Promise((resolve) => {
        video.addEventListener('canplay', () => resolve(true), { once: true });
        video.addEventListener('error', () => resolve(false), { once: true });
    });
    video.addEventListener('ended', () => end('ended', 'ended'));
    video.addEventListener('error', () => end('failed', 'error'));
    video.src = slide.url;

    return {
        element: video,
        ready,

        /** Resolves when the slide should give way; replay() starts a new one. */
        get finished() {
            return finished;
        },

        /** 'ready' | 'playing' | 'ended' | 'failed' | 'released' */
        get state() {
            return state;
        },

        /** Starts playback (the slide is fully in view). A stall or a missing end is guarded by timers. */
        start() {
            if (state === 'ready') {
                play();
            }
        },

        /** Plays an ended video again from the start (it stays on screen for another round). */
        replay() {
            if (state === 'ended') {
                video.currentTime = 0;
                play();
            }
        },

        /** Loops while it is the only slide (PLAYER_SPEC §2): `ended` then never fires and there is no cap. */
        setLoop(loop) {
            if (video.loop === loop || state === 'released') {
                return;
            }
            video.loop = loop;
            if (state === 'playing') {
                if (loop) {
                    clearTimeout(cap);
                } else {
                    armCap();
                }
            }
        },

        /** Playback position 0–1, for the progress bar (R-21). */
        progress() {
            return Number.isFinite(video.duration) && video.duration > 0 ? video.currentTime / video.duration : 0;
        },

        release() {
            end('released', 'released');
            state = 'released';
            stopTimers();
            finish('released');
            video.pause();
            video.removeAttribute('src');
            video.load();
        },
    };
}
