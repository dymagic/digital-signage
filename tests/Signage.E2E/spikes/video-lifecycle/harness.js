// WP-07 spike harness: see harness.html for the strategies. State for the test runner is on window.spike.
const params = new URLSearchParams(location.search);
const strategy = params.get('strategy') ?? 'release';
const target = Number(params.get('transitions') ?? 240);
const media = params.get('media') ?? '/media/';
const transitionMs = Number(params.get('transitionMs') ?? 300);
const imageMs = Number(params.get('imageMs') ?? 300);
const playbackRate = Number(params.get('playbackRate') ?? 4); // plays 3-4 s clips in < 1 s; the decoder still runs
document.documentElement.style.setProperty('--transition-ms', `${transitionMs}ms`);

// mix (default): video, image, video, …; images: no video at all (baseline); videos: back-to-back videos only.
const mix = params.get('mix') ?? 'mixed';
const fullSequence = [
    { type: 'video', src: 'clip-a-h264.mp4' },
    { type: 'image', src: 'image-testsrc2.png' },
    { type: 'video', src: 'clip-b-h264.mp4' },
    { type: 'image', src: 'image-smptebars.png' },
    { type: 'video', src: 'clip-c-h264.mp4' },
    { type: 'image', src: 'image-mandelbrot.png' },
];
const sequence = mix === 'images' ? fullSequence.filter((s) => s.type === 'image')
    : mix === 'videos' ? fullSequence.filter((s) => s.type === 'video')
    : fullSequence;

const stage = document.getElementById('stage');
const state = (window.spike = { strategy, mix, transitions: 0, done: false, errors: [], videoElementsCreated: 0, maxLayers: 0, maxVideosInDom: 0 });
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

// For "reuse": two persistent video elements.
const pool = [];
let poolNext = 0;

function newVideo() {
    const video = document.createElement('video');
    video.muted = true;
    video.playsInline = true;
    video.preload = 'auto';
    state.videoElementsCreated++;
    return video;
}

/** Builds (or for "reuse", re-targets) the layer for a slide and starts preloading it. */
function prepare(slide) {
    const layer = document.createElement('div');
    layer.className = 'slide entering';
    if (slide.type === 'image') {
        const img = document.createElement('img');
        img.src = media + slide.src;
        layer.append(img);
        layer.ready = img.decode().catch(() => {});
    } else {
        let video;
        if (strategy === 'reuse') {
            if (pool.length < 2) {
                pool.push(newVideo());
            }
            video = pool[poolNext];
            poolNext = (poolNext + 1) % 2;
        } else {
            video = newVideo();
        }
        video.src = media + slide.src;
        video.currentTime = 0;
        layer.append(video);
        layer.video = video;
        layer.ready = new Promise((resolve) => {
            if (video.readyState >= 3) {
                resolve();
            } else {
                video.addEventListener('canplay', () => resolve(), { once: true });
                video.addEventListener('error', () => resolve(), { once: true });
            }
        });
    }
    stage.append(layer);
    return layer;
}

/** What happens to a layer once the next one has fully covered it. */
function cover(layer) {
    const video = layer.video;
    if (!video) {
        layer.remove();
        return;
    }
    switch (strategy) {
        case 'release':
            video.pause();
            video.removeAttribute('src');
            video.load();
            layer.remove();
            break;
        case 'remove':
            layer.remove();
            break;
        case 'keep':
            video.pause();
            layer.classList.add('hidden');
            break;
        case 'reuse':
            video.pause();
            // The element itself lives on in the pool; only the wrapper goes.
            layer.remove();
            break;
    }
}

async function play(layer, slide) {
    if (slide.type === 'image') {
        await sleep(imageMs);
        return;
    }
    const video = layer.video;
    video.playbackRate = playbackRate;
    const ended = new Promise((resolve) => {
        video.addEventListener('ended', resolve, { once: true });
        video.addEventListener('error', resolve, { once: true });
    });
    try {
        await video.play();
    } catch (e) {
        state.errors.push(String(e));
    }
    await Promise.race([ended, sleep(10_000)]); // safety cap
}

async function transition(layer) {
    await Promise.race([layer.ready, sleep(3000)]);
    await new Promise((r) => requestAnimationFrame(() => requestAnimationFrame(r)));
    layer.classList.add('animating');
    layer.classList.remove('entering');
    await Promise.race([new Promise((r) => layer.addEventListener('transitionend', r, { once: true })), sleep(transitionMs + 500)]);
    layer.classList.remove('animating');
}

async function run() {
    let index = 0;
    let current = prepare(sequence[0]);
    await transition(current);
    while (state.transitions < target) {
        const slide = sequence[index % sequence.length];
        const nextSlide = sequence[(index + 1) % sequence.length];
        const next = prepare(nextSlide); // preload while the current slide plays
        state.maxLayers = Math.max(state.maxLayers, stage.childElementCount);
        state.maxVideosInDom = Math.max(state.maxVideosInDom, document.querySelectorAll('video').length);
        await play(current, slide);
        await transition(next);
        // Order matters (ADR-0008): release the covered slide first, then (next loop) prepare the one after next,
        // so at most two video elements exist even when videos follow each other.
        cover(current);
        current = next;
        index++;
        state.transitions++;
    }
    state.done = true;
}

run().catch((e) => {
    state.errors.push(String(e));
    state.done = true;
});
