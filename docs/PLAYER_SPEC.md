# Player Specification

> Exact behaviour of `/screen`. The opening spec cares a lot about how this *feels*, so treat these
> values as the agreed baseline. Every number lives in `wwwroot/screen/config.js`.

## 1. Layout

- A full-viewport black stage (`100vw × 100vh`, `overflow: hidden`, no scrollbars, cursor hidden
  after 3 s idle).
- Each slide is an absolutely positioned layer filling the stage. Images and videos use
  `object-fit: cover`, as full-frame content is the norm. A per-slide "fit: contain" is a possible
  later option: do not build it.
- Layers from bottom to top: slides → info overlay → progress bar → identify overlay.

## 2. Carousel transition (R-20)

- The **incoming slide** starts at `transform: translateX(100%)` and animates to `translateX(0)`.
- **Duration:** 900 ms. **Easing:** `cubic-bezier(0.16, 1, 0.3, 1)`, which starts fast and settles
  firmly with no bounce.
  *Note for the product owner:* the opening spec says "firm ease-in". Literal CSS `ease-in`
  accelerates *into* the stop and feels like a slam. The curve above gives the "firm" arrival. To
  change it, edit one constant.
- **Shadow:** the incoming layer carries a subtle shadow on its left edge, e.g.
  `box-shadow: -24px 0 48px -8px rgba(0,0,0,0.45)`. It must be visible on light content and never look
  like a border.
- **Stacking:** the outgoing slide **does not move**. It stays at `translateX(0)` under the incoming
  one (higher `z-index`). Once the incoming slide has fully arrived (`transitionend`, with a
  timeout fallback), the outgoing slide goes to state `covered` and is cleaned up (see §4).
- Animate with `transform` only (GPU composited). No layout-affecting properties. Use
  `will-change: transform` only during the transition.
- **Preloading:** when a slide becomes `active`, the player starts preparing the *next* slide
  off-screen: image decode via `img.decode()`, iframe/srcdoc loading, video `preload="auto"`.
  If the next slide is not ready when its time comes, wait up to 3 s more, then transition anyway.
- With only one playable slide: show it and do not transition to itself. A single video loops.

## 3. Progress bar (R-21)

- A bottom-edge bar, **3 px** high, full width. Fill colour `rgba(255,255,255,0.35)` over a track of
  `rgba(255,255,255,0.08)`. A thin `mix-blend-mode: difference` or a soft dark under-glow keeps it
  visible on white content. It must stay subtle.
- It fills left → right linearly over the slide's duration, starting when the slide is fully in view
  (after the transition). For video, the width follows `currentTime / duration`.
- It is hidden in single video mode, in the empty state and while only one slide is playing.

## 4. Video lifecycle (R-22)

Baseline behaviour (must hold whatever the spike finds):
1. A video slide is created with `muted` (unless `playAudio`), `playsinline`, `preload="auto"` and
   **no** `autoplay`.
2. `play()` is called only when the slide's transition has fully completed.
3. On `ended`: the last frame stays visible, and the next transition starts at once.
4. On `error`, or a stall longer than 8 s without progress: log it, skip to the next slide, and report it in the heartbeat.
5. If the video has no duration metadata, a safety cap (10 min) applies.

**Spike WP-07 must investigate and decide** (record in an ADR), measuring on Chromium in Docker and
noting the Raspberry Pi implications from the literature:
- whether a covered video element should be released by `pause()` → `removeAttribute('src')` →
  `load()` → remove from DOM. That is the hypothesis: it frees decoder and buffer memory, which
  matters on Pi-class hardware.
- whether to reuse one or two `<video>` elements versus creating fresh ones.
- the memory / handle trend over 200+ transitions (use `performance.memory`, the Chrome DevTools
  Protocol via Playwright, or the Docker stats of the browser container).
- how preloading the next video interacts with releasing the current one.

General cleanup: covered non-video slides are removed from the DOM too. At most **3** slide layers
may exist at any time (covered-being-removed, active, next-preparing).

## 5. Slide types

| Type | Render | Notes |
|---|---|---|
| Image | `<img>`, decoded before the transition | On load error, skip it. |
| Video | see §4 | |
| WebPage | `<iframe src sandbox="allow-scripts allow-same-origin allow-forms" referrerpolicy="no-referrer">`. Same-origin is safe here because the site is a different origin from the player. No `allow-top-navigation` or `allow-popups`. | Framing may be refused. On a load timeout of 10 s, still show it (it can't be detected reliably). |
| Html | `<iframe sandbox="allow-scripts" srcdoc="…">` with a base stylesheet that resets margins and sets a black background | Never `allow-same-origin`. |

Pointer events are disabled on all slide content.

## 6. Single video mode (R-14)

One `<video loop muted playsinline>` filling the screen. No carousel, progress bar or transitions.
When the mode or URL changes through an event, cross-fade (300 ms) to the new state. The info and
identify overlays still work.

## 7. Overlays

- **Info overlay (R-30):** bottom-left, 8 px above the progress bar. 14 px system UI font at 1080p
  (scale with `vmin`), white at 55 % opacity with a soft text shadow. Content: `Name · 10.0.3.27`.
- **Identify (R-31):** a 24 px solid red (`#e11d48`) inset border, pulsing in opacity between 100 % and 55 %
  at 1 Hz, with the screen name centred in large (≈ 8 vmin) bold white text on a translucent dark
  pill. Lasts 30 s (from the event), then fades out over 500 ms. A new identify event restarts the timer.
- **Identify all (R-32):** the same name display, without the red border.

## 8. Unpaired / pairing screen (R-40)

A calm dark screen with the product name, "Pair this screen" and the 6-character code in very large
monospace (e.g. `K7P-Q2M`), the server URL, and the instruction "Admin → Screens → Pair screen". The
code refreshes automatically before it expires. Poll every 3 s. A device with a token in the address (`/screen?token=…`) exchanges it
for the cookie first and removes it from the address bar (R-41). Any `401` from the player API returns it to this
screen (R-42).

## 9. Connectivity (R-24, R-25, R-26)

- On boot: if a cached manifest exists in localStorage, start playing it **immediately**, then
  fetch the fresh one.
- Keep an `EventSource` to `/api/player/events` (it auto-reconnects). Also poll the manifest every
  60 s with `If-None-Match`.
- On a new manifest: don't interrupt the current slide. The new sequence takes effect at the next
  transition. Continue from the slide after the current one's id if it still exists, otherwise
  start from the beginning. Mode changes and overlay toggles apply immediately.
- If `appVersion` differs from the loaded player's version: reload the page at the next transition.
- Heartbeat every 30 s.
- While offline: keep cycling, show nothing alarming, and add a tiny dim dot in the info overlay
  only when the overlay is enabled.

## 10. Empty state (R-23)

A dark background with the screen name (small, top-left) and a large thin clock (HH:mm, using the system
time zone from settings), centred. No error text.
