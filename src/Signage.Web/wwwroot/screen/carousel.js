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

const sleep = (duration) => new Promise((resolve) => setTimeout(resolve, duration));
const nextFrame = () => new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)));
const sameContent = (a, b) => a.type === b.type && a.url === b.url && a.html === b.html && a.playAudio === b.playAudio;

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

        this.manifest = null;
        this.pending = null; // a new manifest takes effect at the next transition (PLAYER_SPEC §9)
        this.current = null; // { slide, layer, item }
        this.lastIds = '';
        this.position = -1;
        this.beforeTransition = null; // e.g. the daily reload: async () => void, run once at the next transition
        this.wake = null;
        this.stopped = false;
    }

    /** Uses this manifest from the next transition on (immediately if nothing plays yet). */
    setManifest(manifest) {
        if (!this.manifest) {
            this.manifest = manifest;
        } else {
            this.pending = manifest;
        }
        this.wake?.();
    }

    /** True while a slide is shown and two or more take turns (so a transition will come). */
    get isCycling() {
        return this.current !== null && playable(this.manifest, Date.now()).length > 1;
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
        this.hideProgress();
        hideEmpty(this.stage);
    }

    async run() {
        while (!this.stopped) {
            if (this.pending) {
                this.manifest = this.pending;
                this.pending = null;
            }

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
                // One playable slide: keep showing it, no transition to itself, no progress bar.
                this.hideProgress();
                document.body.dataset.mode = 'player';
                await this.idle(config.staticRecheckMs);
                continue;
            }

            const incoming = this.prepare(this.pickNext(list));
            await this.dwell(this.current.slide);
            if (this.stopped) {
                this.release(incoming.layer);
                return;
            }
            if (this.beforeTransition) {
                const task = this.beforeTransition;
                this.beforeTransition = null;
                await task();
            }
            await this.advance(incoming);
        }
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
                    return;
                }
            }
            if (isActive(candidate.slide, Date.now()) && (await this.isReady(candidate))) {
                await this.transition(candidate);
                return;
            }
            this.release(candidate.layer);
            const list = playable(this.manifest, Date.now());
            if (attempt >= list.length) {
                return;
            }
            const next = this.pickNext(list);
            candidate = next.id === this.current.slide.id ? null : this.prepare(next);
        }
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

    /** Shows the active slide for its duration, with the progress bar (R-21). */
    async dwell(slide) {
        const duration = ms((slide.durationSeconds ?? this.manifest?.settings?.defaultDuration ?? 10) * 1000);
        this.startProgress(duration);
        await sleep(duration);
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
    }

    release(layer) {
        layer.item?.release();
        layer.remove();
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
        this.progress.hidden = true;
        this.progressFill.style.transition = 'none';
        this.progressFill.style.transform = 'scaleX(0)';
    }

    showEmptyState() {
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
