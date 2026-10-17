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

**Safety net: a daily reload** (user-visible; confirmed by the product owner on 2026-10-17). Long runs (data below) show that releasing videos itself doesn't leak: 2000 back-to-back
videos add about 0.03 MB each. A mix of images and videos still grows (about 0.09 MB per transition over 2000
transitions; +147 MB from transition 200 to 1000 and +104 MB from 1000 to 1800, two equal 800-transition spans, leaving
out the last sample, which was taken after the loop had stopped). That looks like Chromium's caches filling
up, but a plateau isn't proven. If the growth were linear, 10 s slides (8,640 transitions a day) would add about
0.7 GB a day, too much for a Pi left alone for weeks. So the player **reloads itself once a
day**: after 20 hours of uptime, inside a night window (02:00–05:00 in the system time zone), or after 26 hours
whatever the time. It reloads only when the server answers at that moment, so an offline screen keeps cycling (R-24)
instead of landing on the browser's error page. A carousel reloads at a transition, and screens that never
transition reload at the check. It restarts from its cached manifest, so the screen shows a moment of black and then
carries on. PLAYER_SPEC §9 has the details. A reload resets all renderer memory, so any slow growth, from video or anything else,
stays bounded at one day's worth.

## Consequences
- Covered videos lose their decoded frames immediately. That's fine: they are covered.
- WP-13's soak runs the shipped player for 30 minutes (about 1,000–1,400 transitions; videos play at normal speed)
  as a regression guard: it fails if growth exceeds twice the rate measured here. The daily reload, not the soak,
  is what bounds memory. See BACKLOG WP-13.
- Not measured: whether the next video was ready (`canplay`) before its turn. The harness waits up to 3 s like the
  player will. WP-09 tests the player's behaviour when a video is late or fails.
- Raspberry Pi (from public documentation and reports, not measured here): the Pi 4 decodes H.264 in hardware
  through V4L2, with a limited number of decoder instances and limited CMA memory. Pages that keep a paused
  element with a `src` can hold a hardware decoder until the element is garbage-collected or Chromium suspends it.
  On a constrained device that shows up as failures to start the next video. The Pi 5 has no H.264 hardware decoder
  (software decoding, CPU and RAM bound), so freeing buffers promptly matters there too. Releasing explicitly, with at
  most two `<video>` elements, is the defensive choice on both. Back-to-back videos are measured: at most 2 elements,
  every pipeline destroyed at cover time.

## Data
**Method.** The throwaway harness (`tests/Signage.E2E/spikes/video-lifecycle/`, not shipped) uses the planned
transition (translateX + shadow; 300 ms here to save time). It cycles 3 H.264 + AAC 1080p30 clips (3–4 s) and 3
full-HD PNGs, in one of three mixes: alternating video and image, videos only (back to back), or images only. It
prepares the next slide while the current one plays, and releases the covered slide *before* preparing the one after
next. Runner: `tests/Signage.E2E/Spikes/VideoLifecycleSpike.cs`. Every N transitions it forces a GC via CDP and records
the JS heap, DOM nodes, `<video>` elements in the DOM, Chromium's media-pipeline create/destroy log events
(`kWebMediaPlayerCreated`/`Destroyed` via the CDP `Media` domain), and the proportional set size (PSS) of all browser
processes from `/proc/*/smaps_rollup`. Playwright tracing is **off** during measurements.
**Caveat:** the forced GC before each sample flatters *remove*, whose pipelines are only destroyed by GC. Without
it, *remove* would hold more than measured.
**Environment:** Playwright 1.63 headless Chromium 153 in `mcr.microsoft.com/playwright/dotnet:v1.63.0-noble`,
software decoding and rendering, x86_64 Linux host. A codec probe (`CodecProbe.cs`) shows this Chromium build plays
H.264 + AAC in MP4, so E2E tests use realistic media. Raw data: `artifacts/e2e/spike/*.csv|json` (regenerable).

**Strategies, 240 transitions (120 video plays), video/image mix, 4× playback, 300 ms images.** PSS in MB:

| Strategy | 0 | 60 | 120 | 180 | 240 | `<video>` in DOM at 240 | Pipelines destroyed / created |
|---|---|---|---|---|---|---|---|
| keep | 246 | 2015 | 3619 | 5228 | **6686** | 121 | 0 / 120 |
| remove | 246 | 504 | 699 | 825 | 820 | 1 | 120 / 120 (at GC) |
| reuse | 297 | 433 | 498 | 556 | 592 | 1 (2 elements in total) | 119 / 120 |
| **release** | 389 | 386 | 470 | 531 | **578** | 1 | **120 / 120 (at cover time)** |

The JS heap stayed at 1.3–1.4 MB in every case. With *keep*, DOM nodes grew from 171 to 18,051. In all other
strategies they stayed at 171–322.

**Release over 2000 transitions, sampled every 200, 8× playback, 200 ms images.** PSS in MB:

| Mix | 0 | 200 | 400 | 600 | 800 | 1000 | 1200 | 1400 | 1600 | 1800 | 2000 |
|---|---|---|---|---|---|---|---|---|---|---|---|
| video/image | 420 | 381 | 442 | 499 | 505 | 528 | 555 | 568 | 593 | 632 | 606 |
| videos only | 246 | 324 | 326 | 340 | 338 | 343 | 355 | 359 | 364 | 377 | 334 |
| images only | 196 | 265 | 273 | 282 | 267 | 275 | 276 | 276 | 269 | 277 | 278 |

Videos only: at most 2 `<video>` elements at any time, and 2000 of 2000 pipelines destroyed.
