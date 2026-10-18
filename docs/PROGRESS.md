# Progress & hand-off notes

> Written by the developer for its future self (context resets between sessions). The reviewer
> does not read this file.

## Status
Milestone 1: in progress (WP-00–WP-07 done). Project docs, skills and the reviewer agent were set up on 2026-10-16.

## Current package
- Package: WP-10 Live updates, heartbeat & status — branch `wp-10-live-updates` — status In review (round 1 requested)
- Plan: server: `PostgresScreenNotifier` (pg_notify on `signage_events`, chunked payloads), `ScreenEventHub` (per-replica
  SSE subscribers by screen), `ScreenEventListener` (BackgroundService, LISTEN, reconnect + "all changed" after a gap),
  `GET /api/player/events` (SSE, keep-alive 15 s), `POST /api/player/heartbeat` (ExecuteUpdate of LastSeen/IP/UA/version,
  no version bump; problems logged), status columns in the screens list with htmx refresh (online = heartbeat < 90 s),
  `/screen` injects the app version, forwarded headers from config. Player: EventSource with recreate after a closed
  stream, fetch on event and on open, heartbeat every 30 s with problems (video/single-video failures), appVersion
  reload at the next transition. WP-09 follow-ups.
- Tests: integration (notifier → SSE on two app instances, heartbeat fields + no conflict, offline after 90 s with fake
  time, forwarded headers, events auth), E2E (edit reaches the screen within 5 s, appVersion reload).
- Done: notifier/hub/listener/SSE, heartbeat + status list, version stamp + reload, forwarded headers, WP-09
  follow-ups, ADR-0010, docs. Integration 14 + E2E (LiveUpdateTests 3, 4 new VideoTests).
- Full-suite flake found: E2E devices share one IP and hit the 10/min token exchange limit. The exchange now has its own
  configurable limit (default unchanged), E2E sets 120; open question for the PO.
- Self-check green: build 0 warnings, `dotnet test` 460, E2E 55 (8 opt-in spike skips), twice.
- Next step: handle the review round 1 verdict (docs/reviews/WP-10-review-1.md).
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
- WP-09 — video slides (fresh element, play after transition, progress follows playback, ended/error/stall/cap, replay and loop when alone, muted fallback for sound) and single video mode (cross-fade, live mode switch, retries, layer tracking); ADR-0009; 2 rounds, 2 MINOR + 2 NIT deferred to WP-10 — 975c459

## Open questions for the product owner
- (answered) WP-02 round 4 approved by the product owner on 2026-10-17.
- (answered) WP-07 daily player reload confirmed by the product owner on 2026-10-17 (ADR-0008).
- (answered) WP-07: the PO confirmed the daily reload stands with the corrected ~1.3 GB/day worst case and approved review round 4 (2026-10-17).
- (open, not blocking) WP-10: screen address exchanges are limited to 10 per client IP and minute (ADR-0007). Many
  kiosks behind one NAT that boot together with `/screen?token=…` (a power cut) then take several minutes to all
  exchange; they play their cached content meanwhile, so nobody sees it, but a first-ever boot would wait. The limit is
  now configurable (`SIGNAGE__SCREENADDRESSEXCHANGESPERMINUTE`, default 10; E2E uses 120). Keep 10 as the default, or
  raise it (e.g. 60, like pairing polls)? The token is 256 bits, so the limit is about load, not guessing.

## Notes
- The shell is zsh and `grep` is aliased to ugrep: use `command grep` in scripts. Quote `-?`.
- xUnit v3 on MTP: filters are `--filter-class/--filter-method/--filter-trait`; exit code 8 = an assembly matched no tests.
- E2E: the app is `http://signage-web:8080` inside Docker (Chromium forces HTTPS for host `app`). E2E runner args are xUnit runner args (`-trait "Category=X"`).
- The user `hello-world` container `keen_nobel` is the product owner's, not ours; leave it.
