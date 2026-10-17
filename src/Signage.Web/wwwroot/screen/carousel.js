// The carousel engine (PLAYER_SPEC §2–§3, R-20, R-21, R-23).
//
// Layers: each slide is a .slide element with data-state:
//   entering  prepared off-screen at translateX(100%), then animating in (with the left-edge shadow)
//   active    fully in view
//   leaving   the outgoing slide while the next one slides over it — it never moves
//   covered   fully covered; released and removed straight away
// At most three layers exist (covered-being-removed, active, next). The covered slide is released *before* the slide
// after the new one is prepared (ADR-0008).
import { config, ms } from './config.js';
import { isActive, playable } from './schedule.js';
import { createSlide } from './slides/index.js';
import { showEmpty, hideEmpty } from './empty.js';
import { SingleVideo } from './singlevideo.js';

const sleep = (duration) => new Promise((resolve) => setTimeout(resolve, duration));
const nextFrame = () => new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)));
const sameContent = (a, b) => a.id === b.id && a.type === b.type && a.url === b.url && a.html === b.html && a.playAudio === b.playAudio;
// What has to change at once instead of at the next transition (PLAYER_SPEC §9): the mode and the single video's URL.
const modeKey = (manifest) => (manifest?.mode === 'SingleVideo' ? `single:${manifest.singleVideoUrl ?? ''}` : 'carousel');

export class Carousel {
    /** @param {HTMLElement} stage */
    constructor(stage) {
        this.stage = stage;
        this.slidesEl = document.createElement('div');
        this.slidesEl.className = 'slides';
        this.progress = document.createElement('div');
        this.progress.className = 'progress';
        this.progress.dataset.testid = 'progress';
        this.progressFill = document.createElement('div');
        this.progressFill.className = 'fill';
        this.progress.append(this.progressFill);
        stage.append(this.slidesEl, this.progress);
        this.single = new SingleVideo(stage);

        this.manifest = null;
        this.pending = null; // a new manifest takes effect at the next transition (PLAYER_SPEC §9)
        this.current = null; // { slide, layer, item }
        this.lastIds = '';
        this.position = -1;
        this.beforeTransition = null; // e.g. the daily reload: async () => void, run once at the next transition
        this.wake = null;
        this.interrupt = null; // ends a dwell early when the mode changes
        this.following = null; // the video item the progress bar follows
        this.stopped = false;
    }

    /** Uses this manifest from the next transition on (immediately if nothing plays yet). */
    setManifest(manifest) {
        if (!this.manifest) {
            this.manifest = manifest;
        } else {
            this.pending = manifest;
            if (modeKey(manifest) !== modeKey(this.manifest)) {
                this.interrupt?.();
            }
        }
        this.wake?.();
    }

    /** True while a slide is shown and two or more take turns (so a transition will come). */
    get isCycling() {
        return this.manifest?.mode !== 'SingleVideo' && this.current !== null && playable(this.manifest, Date.now()).length > 1;
    }

    /** Runs `task` right before the next transition (used by the daily reload). */
    runBeforeNextTransition(task) {
        this.beforeTransition = task;
    }

    stop() {
        this.stopped = true;
        this.wake?.();
        for (const layer of [...this.slidesEl.children]) {
            this.release(layer);
        }
        this.single.hide();
        this.hideProgress();
        hideEmpty(this.stage);
    }

    async run() {
        while (!this.stopped) {
            if (this.pending) {
                this.manifest = this.pending;
                this.pending = null;
            }

            if (this.manifest?.mode === 'SingleVideo') {
                this.showSingleVideo();
                await this.idle(config.staticRecheckMs);
                continue;
            }
            this.single.hide(); // back from single video mode: the carousel starts underneath while it fades out

            const list = playable(this.manifest, Date.now());
            if (list.length === 0) {
                this.showEmptyState();
                await this.idle(config.staticRecheckMs);
                continue;
            }

            if (!this.current) {
                if (!(await this.showFirst(list))) {
                    // Nothing in the sequence can be shown (e.g. every image host unreachable): the empty state, and a
                    // calm retry instead of hammering the hosts.
                    this.showEmptyState();
                    await this.idle(ms(config.nothingShowableRetryMs));
                }
                continue;
            }

            if (list.length === 1 && list[0].id === this.current.slide.id) {
                // One playable slide: keep showing it, no transition to itself, no progress bar. If it was edited, the new
                // version slides in over the old one once it is ready (PLAYER_SPEC §2).
                if (!sameContent(list[0], this.current.slide)) {
                    const edited = this.prepare(list[0]);
                    if (await this.isReady(edited)) {
                        await this.transition(edited);
                    } else {
                        this.release(edited.layer); // the old version stays; try the new one again later
                        await this.idle(ms(config.nothingShowableRetryMs));
                        continue;
                    }
                }
                this.current.slide = list[0]; // e.g. a new duration
                this.current.item.setLoop?.(true); // a lone video loops
                await this.keepCurrentPlaying();
                this.hideProgress();
                document.body.dataset.mode = 'player';
                await this.idle(config.staticRecheckMs);
                continue;
            }

            this.current.item.setLoop?.(false);
            const incoming = this.prepare(this.pickNext(list));
            await this.dwell(this.current);
            if (this.stopped || (this.pending && modeKey(this.pending) !== modeKey(this.manifest))) {
                this.release(incoming.layer); // stopped, or the mode changed: that applies at once
                continue;
            }
            if (this.beforeTransition) {
                const task = this.beforeTransition;
                this.beforeTransition = null;
                await task();
            }
            if (!(await this.advance(incoming))) {
                if (this.isStale()) {
                    // The current slide has expired or was removed, and nothing else could be shown: don't keep it (R-03).
                    this.showEmptyState();
                    await this.idle(ms(config.nothingShowableRetryMs));
                } else {
                    await this.keepCurrentPlaying();
                }
            }
        }
    }

    /**
     * The current slide stays on screen for another round: a video that ended plays again, and one that failed (error,
     * stall, cap) is replaced by a fresh element after a calm pause (ADR-0008, ADR-0009). Never a busy loop.
     */
    async keepCurrentPlaying() {
        const item = this.current?.item;
        if (item?.state === 'ended') {
            item.replay();
            return;
        }
        if (item?.state !== 'failed') {
            return;
        }
        await this.idle(ms(config.nothingShowableRetryMs));
        if (this.stopped || this.current?.item !== item) {
            return;
        }
        const fresh = this.prepare(this.current.slide);
        const ok = await Promise.race([fresh.item.ready, sleep(ms(config.preloadGraceMs)).then(() => false)]);
        if (!ok || this.stopped || this.current?.item !== item) {
            this.release(fresh.layer);
            return;
        }
        fresh.layer.dataset.state = 'active'; // swapped in place, no transition to itself
        this.release(this.current.layer);
        this.current = fresh;
        fresh.item.start();
    }

    /** True when the slide on screen has left a non-empty sequence (expired or removed). */
    isStale() {
        const list = playable(this.manifest, Date.now());
        return this.current !== null && list.length > 0 && !list.some((s) => s.id === this.current.slide.id);
    }

    /** Nothing on screen yet: shows the first slide of the list that can be shown. False if none could. */
    async showFirst(list) {
        for (let attempt = 0; attempt < list.length && !this.stopped; attempt++) {
            const incoming = this.prepare(this.pickNext(list));
            if (await this.isReady(incoming)) {
                await this.transition(incoming);
                return true;
            }
            this.release(incoming.layer);
        }
        return false;
    }

    /**
     * After the dwell: moves on to the next slide that can be shown, without dwelling on the current slide again
     * (PLAYER_SPEC §2, §9). A newer manifest decides what comes next; a slide that has failed to load or whose
     * schedule has ended meanwhile is skipped (R-03). If nothing else can be shown, the current slide stays.
     */
    async advance(incoming) {
        let candidate = incoming;
        for (let attempt = 1; candidate && !this.stopped; attempt++) {
            if (this.pending) {
                candidate = this.adoptPending(candidate);
                if (!candidate) {
                    return false;
                }
            }
            if (isActive(candidate.slide, Date.now()) && (await this.isReady(candidate))) {
                await this.transition(candidate);
                return true;
            }
            this.release(candidate.layer);
            const list = playable(this.manifest, Date.now());
            if (attempt >= list.length) {
                return false;
            }
            const next = this.pickNext(list);
            candidate = next.id === this.current.slide.id ? null : this.prepare(next);
        }
        return false;
    }

    /** Applies the pending manifest; returns the slide to show next (the prepared one if it still is) or null. */
    adoptPending(candidate) {
        this.manifest = this.pending;
        this.pending = null;
        const list = playable(this.manifest, Date.now());
        if (list.length === 0) {
            this.release(candidate.layer);
            return null;
        }
        const ids = list.map((s) => s.id).join(',');
        const next = ids === this.lastIds ? list[this.position] : this.pickNext(list);
        if (list.length === 1 && next.id === this.current.slide.id) {
            this.release(candidate.layer);
            return null;
        }
        if (sameContent(next, candidate.slide)) {
            candidate.slide = next; // e.g. a new duration: the layer can stay
            return candidate;
        }
        this.release(candidate.layer);
        return this.prepare(next);
    }

    /** The slide after the current one; after a list change, the one after the current slide's id, else the first. */
    pickNext(list) {
        const ids = list.map((s) => s.id).join(',');
        if (ids !== this.lastIds) {
            const index = this.current ? list.findIndex((s) => s.id === this.current.slide.id) : this.position;
            this.position = Math.min(index, list.length - 1);
            this.lastIds = ids;
        }
        this.position = (this.position + 1) % list.length;
        return list[this.position];
    }

    prepare(slide) {
        const item = createSlide(slide);
        const layer = document.createElement('div');
        layer.className = 'slide';
        layer.dataset.state = 'entering';
        layer.dataset.slideId = slide.id;
        layer.dataset.type = slide.type;
        layer.append(item.element);
        layer.item = item;
        this.slidesEl.append(layer);
        return { slide, layer, item };
    }

    /** True when the slide is ready, or still loading after the grace period (PLAYER_SPEC §2); false if it failed. */
    isReady(incoming) {
        return Promise.race([incoming.item.ready, sleep(ms(config.preloadGraceMs)).then(() => true)]);
    }

    /**
     * Shows the active slide with the progress bar (R-21): for its duration, or for a video until it finishes (the bar
     * follows playback). A mode change ends it early.
     */
    async dwell(current) {
        const interrupted = new Promise((resolve) => { this.interrupt = resolve; });
        if (current.item.finished) {
            this.followProgress(current.item);
            await Promise.race([current.item.finished, interrupted]);
        } else {
            const duration = ms((current.slide.durationSeconds ?? this.manifest?.settings?.defaultDuration ?? 10) * 1000);
            this.startProgress(duration);
            await Promise.race([sleep(duration), interrupted]);
        }
        this.interrupt = null;
    }

    /** Single video mode (R-14): the video over everything; the carousel slide goes once the video is visible. */
    showSingleVideo() {
        const url = this.manifest.singleVideoUrl;
        if (!url) {
            this.single.hide();
            this.showEmptyState();
            return;
        }
        hideEmpty(this.stage);
        this.hideProgress();
        document.body.dataset.mode = 'single-video';
        this.single.show(url);
        // The carousel slide goes once the video has faded in, or at once if the video can't play (black until a retry
        // succeeds: stale carousel content must not stay on a screen set to single video).
        if (this.current && (this.single.visible || this.single.failed)) {
            this.release(this.current.layer);
            this.current = null;
            this.lastIds = ''; // back in carousel mode, start from the beginning
            this.position = -1;
        }
    }

    async transition(incoming) {
        hideEmpty(this.stage);
        document.body.dataset.mode = 'player';
        const outgoing = this.current;
        this.hideProgress();
        if (!outgoing) {
            // First slide: just show it.
            incoming.layer.dataset.state = 'active';
        } else {
            outgoing.layer.dataset.state = 'leaving';
            await nextFrame();
            incoming.layer.classList.add('animating');
            incoming.layer.style.transform = 'translateX(0)';
            await new Promise((resolve) => {
                const done = () => resolve();
                incoming.layer.addEventListener('transitionend', done, { once: true });
                setTimeout(done, ms(config.transitionMs) + config.transitionFallbackExtraMs);
            });
            incoming.layer.classList.remove('animating');
            incoming.layer.style.transform = '';
            incoming.layer.dataset.state = 'active';
            outgoing.layer.dataset.state = 'covered';
            this.release(outgoing.layer); // before anything new is prepared (ADR-0008)
        }
        this.current = incoming;
        incoming.item.start?.(); // videos play only once fully in view (R-22)
    }

    release(layer) {
        layer.item?.release();
        layer.remove();
    }

    /** The progress bar follows a video's playback position (R-21). */
    followProgress(item) {
        const fill = this.progressFill;
        this.progress.hidden = false;
        fill.style.transition = 'none';
        fill.style.transform = 'scaleX(0)';
        this.following = item;
        const step = () => {
            if (this.following !== item) {
                return;
            }
            fill.style.transform = `scaleX(${item.progress()})`;
            requestAnimationFrame(step);
        };
        requestAnimationFrame(step);
    }

    startProgress(duration) {
        const fill = this.progressFill;
        this.progress.hidden = false;
        fill.style.transition = 'none';
        fill.style.transform = 'scaleX(0)';
        void fill.offsetWidth; // apply the reset before animating
        fill.style.transition = `transform ${duration}ms linear`;
        fill.style.transform = 'scaleX(1)';
    }

    hideProgress() {
        this.following = null;
        this.progress.hidden = true;
        this.progressFill.style.transition = 'none';
        this.progressFill.style.transform = 'scaleX(0)';
    }

    showEmptyState() {
        if (this.stopped) {
            return; // e.g. pairing or a preview message has the stage now
        }
        if (this.current) {
            this.release(this.current.layer);
            this.current = null;
        }
        this.hideProgress();
        document.body.dataset.mode = 'empty';
        showEmpty(this.stage, this.manifest);
    }

    /** Waits a while, or until a new manifest arrives. */
    idle(duration) {
        return new Promise((resolve) => {
            const timer = setTimeout(done, duration);
            function done() {
                clearTimeout(timer);
                resolve();
            }
            this.wake = () => {
                this.wake = null;
                done();
            };
        });
    }
}
