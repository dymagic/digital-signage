# Progress & hand-off notes

> Written by the developer for its future self (context resets between sessions). The reviewer
> does not read this file.

## Status
Milestone 1: in progress (WP-00–WP-07 done). Project docs, skills and the reviewer agent were set up on 2026-10-16.

## Current package
- Package: WP-09 Video & single video mode — branch `wp-09-video` — status In review (round 1 requested)
- Plan: `slides/video.js` (fresh <video>, muted unless playAudio, playsinline, preload=auto, no autoplay; ready on
  canplay, false on error; start() after the transition; finished on ended / error / 8 s stall / 10 min cap; release per
  ADR-0008); carousel dwells on `finished` with the progress bar following playback; a lone video loops;
  `singlevideo.js` (mode SingleVideo: one looping <video>, 300 ms cross-fade on mode/URL change, applied at once: the
  dwell is interrupted by a mode change). WP-08 follow-ups. E2E with testmedia clips: currentTime 0 during the
  transition, next on ended, broken URL skipped, single video loops without progress bar, live mode switch.
- Risks: headless Chromium codec support (H.264 confirmed in WP-07), timing flakiness with real-time video.
- Done: video.js, singlevideo.js, carousel (start after transition, dwell until finished, progress follows playback,
  lone video loops, mode change interrupts the dwell), WP-08 follow-ups; VideoTests (3) + ClockTests stale test written.
- Player E2E 25/25 (video + clock tests 3 repeat runs green); docs: PLAYER_SPEC §2/§4/§6, ARCHITECTURE, ADR-0009.
- Self-check green: build 0 warnings, `dotnet test` 433, E2E 43 (8 opt-in spike skips); single-video screenshot checked.
- Next step: handle the review round 1 verdict (docs/reviews/WP-09-review-1.md).
- Last checkpoint: (this commit)

<!-- While a package is in flight, keep this shape and update it at every checkpoint:
- Package: WP-xx <title> — branch `wp-xx-…` — status In progress | In review (round n requested)
- Plan: files / tests / risks
- Done: …
- Half-done: … (what state it is in, whether the build or tests are red)
- Next step: one concrete action
- Last checkpoint: <commit sha>
-->


## Completed
_(one line per package: WP-xx — outcome — merge commit)_
- WP-00 — solution, health endpoints, 3 test projects (xUnit v3/MTP), Dockerfile (chiseled, multi-arch), compose, Playwright E2E runner, htmx vendored; APPROVE round 1 (3 MINOR + 3 NIT deferred to WP-01) — 37325f9
- WP-01 — EF model (UUIDv7, snake_case, ICU ci names, xmin), InitialCreate (reversible), start-up initializer, DP keys in DB, timestamp interceptor, SequenceResolver + 41 unit tests; APPROVE round 2 (2 NIT deferred to WP-03) — 740ebf1
- WP-02 — cookie auth (per-request stamp validation), lockout via atomic claims in user_lockouts, setup + bootstrap under advisory lock, user admin with self/last-admin guards, settings page, layout + i18n (resx coverage test), E2E admin; app-managed user version; start-up advisory lock; 4 review rounds (PO approved round 4), 1 MINOR + NITs deferred to WP-05 — 677bb74
- WP-03 — slide library: validator + schedule clock (DST rules), CRUD pages with sandboxed previews, search, toggle, delete-in-use; ADR-0005; APPROVE round 1 (4 MINOR + 5 NIT deferred to WP-04/WP-08/WP-12) — d6c6a9e
- WP-04 — playlists: CRUD, ordered entries via htmx page handlers (dense, versioned, oob version), native drag & drop, delete renumbering; ADR-0006; APPROVE round 1 (3 MINOR + 1 NIT deferred to WP-05) — fe33f04
- WP-05 — screens + groups with admin/editor split per handler, composition lists (htmx, versioned, focus kept), resolved sequence page == resolver, IScreenNotifier (no-op) wired into all features, app-managed screen version; 3 review rounds, 1 NIT deferred to WP-06 — 20d1742
- WP-06 — pairing (codes, one-time token minted at delivery, re-pair, revoke, cancelling pending claims), screen address + token exchange with retry, HttpOnly screen cookie auth, rate limits, player pairing screen; ADR-0007; APPROVE round 2 (1 MINOR + 2 NIT deferred to WP-08) — 2999e75
- WP-07 — spike: release strategy (fresh <video>, pause/removeAttribute src/load/remove, release before preparing), daily reload confirmed by the PO, soak spec for WP-13; ADR-0008; 4 rounds (PO approved round 4), 2 NIT deferred to WP-08 — a11619e
- WP-08 — player core: manifest (ETag/304, weak match) + admin preview, ES-module player (carousel with no double dwell, skipping, 3-layer cap, shadow only while animating, progress bar, empty state, offline cache incl. token boot, client schedule re-check, daily reload), demo seed under advisory lock, WP-03/06/07 follow-ups; 3 rounds, 1 MINOR + 3 NIT deferred to WP-09 — 40dff75

## Open questions for the product owner
- (answered) WP-02 round 4 approved by the product owner on 2026-10-17.
- (answered) WP-07 daily player reload confirmed by the product owner on 2026-10-17 (ADR-0008).
- (answered) WP-07: the PO confirmed the daily reload stands with the corrected ~1.3 GB/day worst case and approved review round 4 (2026-10-17).

## Notes
- The shell is zsh and `grep` is aliased to ugrep: use `command grep` in scripts. Quote `-?`.
- xUnit v3 on MTP: filters are `--filter-class/--filter-method/--filter-trait`; exit code 8 = an assembly matched no tests.
- E2E: the app is `http://signage-web:8080` inside Docker (Chromium forces HTTPS for host `app`). E2E runner args are xUnit runner args (`-trait "Category=X"`).
- The user `hello-world` container `keen_nobel` is the product owner's, not ours; leave it.
