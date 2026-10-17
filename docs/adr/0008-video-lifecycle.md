# ADR-0008: Video lifecycle (WP-07 spike)

- Status: Accepted
- Date: 2026-10-17
- Work package: WP-07
- Requirements: R-22 (and R-20, R-24 for long-running stability)

## Context
PLAYER_SPEC §4 asks what to do with a `<video>` once the next slide has covered it, and whether to reuse
elements. Screens run for weeks, and Pi-class devices have little memory and a small number of hardware
decoders. The hypothesis was that `pause()` → `removeAttribute('src')` → `load()` → remove from the DOM releases
decoder and buffer memory promptly.

## Options considered
1. **keep**: pause and hide covered videos, never remove them (the "do nothing" baseline).
2. **remove**: a fresh `<video>` per video slide; when covered, only remove it from the DOM.
3. **reuse**: two persistent `<video>` elements, used in turn; when covered, pause; the next use swaps `src`.
4. **release**: a fresh `<video>` per video slide; when covered, `pause()` → `removeAttribute('src')` → `load()` →
   remove from the DOM.

## Decision
**Option 4, release.** Every video slide gets a fresh `<video>` element. Once the next slide has fully covered it,
the player calls `pause()`, `removeAttribute('src')`, `load()`, then removes the layer. Only the *next* slide is
prepared while the current one plays (one upcoming video at `preload="auto"`), so with the 3-layer cap no more than
two video elements exist at any time: the playing one and the one being prepared. PLAYER_SPEC §4 is updated.

Why not reuse: it saved nothing measurable against release (more PSS, in fact), and it ties two slides to one
element, so a late `ended`/`error` event from the previous `src` can land on the next slide. Fresh elements keep
each slide's listeners and timers scoped to its own element.

## Consequences
- Covered videos lose their decoded frames immediately. That's fine: they are covered.
- The WP-13 30-minute soak repeats this measurement on the real player.
- Raspberry Pi (from public documentation and reports; not measured here): the Pi 4 decodes H.264 in hardware
  through V4L2, with a limited number of decoder instances and limited CMA memory. Players that keep a paused
  element with a `src` can hold a hardware decoder until the element is garbage-collected or Chromium suspends it.
  On a constrained device that shows up as failures to start the next video. The Pi 5 has no H.264 hardware decoder
  (software decoding, CPU and RAM bound), so freeing buffers promptly matters there too. Releasing explicitly, as
  decided, is the defensive choice on both. Keep test clips at 1080p H.264 High profile, which is what such
  devices play best.

## Data
**Method.** The throwaway harness (`tests/Signage.E2E/spikes/video-lifecycle/`, not shipped) uses the planned
transition (translateX + shadow; 300 ms here to save time) and cycles video and image slides:
3 H.264 1080p30 clips (3–4 s, played at 4× so the decoder runs fully but faster) and 3 full-HD PNGs (300 ms each).
It prepares the next slide while the current one plays. Runner: `tests/Signage.E2E/Spikes/VideoLifecycleSpike.cs`
(`SIGNAGE_E2E_SPIKE=1 scripts/test-e2e.sh -trait "Category=Spike"`). Every 20 transitions it forces a GC via CDP and
records the JS heap, DOM nodes, `<video>` elements in the DOM, Chromium media-player create/destroy events
(CDP `Media` domain), and the proportional set size (PSS) of all browser processes from `/proc/*/smaps_rollup`.
**Environment:** Playwright 1.63 headless Chromium 153 in `mcr.microsoft.com/playwright/dotnet:v1.63.0-noble`,
software decoding and rendering, on an x86_64 Linux host. Raw data: `artifacts/e2e/spike/*.csv|json`
(regenerable).

**240 transitions (120 video plays) per strategy** — browser PSS in MB:

| Strategy | 0 | 60 | 120 | 180 | 240 | `<video>` in DOM at 240 | Players destroyed / created |
|---|---|---|---|---|---|---|---|
| keep | 282 | 2034 | 3653 | 5224 | **6698** | 121 | 0 / 120 |
| remove | 277 | 491 | 623 | 763 | 839 | 1 | 119 / 120 (destroyed lazily, at GC) |
| reuse | 330 | 457 | 514 | 565 | 612 | 1 (2 elements created in total) | 119 / 120 |
| **release** | 383 | 407 | 457 | 519 | **569** | 1 | **120 / 120 (each destroyed at cover time)** |

The JS heap stayed at 1.3–1.4 MB in every case. With *keep*, DOM nodes grew from 181 to 18,541. In all other
strategies they stayed at 181–334.

**Does release level off? 1200 transitions (600 video plays) vs. the same loop with images only** — PSS in MB:

| Transitions | 0 | 100 | 200 | 300 | 400 | 600 | 800 | 1000 | 1200 |
|---|---|---|---|---|---|---|---|---|---|
| release | 388 | 375 | 429 | 461 | 464 | 507 | 535 | 540 | 569 |
| images only | 215 | 282 | 289 | 298 | 308 | 317 | 317 | 319 | 319 |

Release grows about 0.3 MB per transition early on and about 0.1 MB per transition later (+29 MB over the last
300 transitions). The image-only baseline levels off at about 320 MB. The residual growth is small and slowing.
It is consistent with Chromium's media and cache pools filling up, not with a per-video leak: no elements, nodes or
players accumulate. The WP-13 soak runs the shipped player to confirm.
