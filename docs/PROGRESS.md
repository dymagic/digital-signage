# Progress & hand-off notes

> Written by the developer for its future self (context resets between sessions). The reviewer
> does not read this file.

## Status
Milestone 1: in progress (WP-00–WP-07 done). Project docs, skills and the reviewer agent were set up on 2026-10-16.

## Current package
- Package: WP-08 Player core — branch `wp-08-player-core` — status In review (round 2 requested)
- Plan: manifest endpoint (ETag/304, schedule filtered client-side, expired slides dropped) + admin preview manifest;
  player ES modules in `wwwroot/screen/` (carousel, slides, schedule, storage, connection, pairing, empty, reload);
  demo seed; WP-06/WP-03/WP-07 follow-ups (see BACKLOG). Tests: unit (base style parity, resolver), integration
  (`Player/ManifestTests`, `DemoSeedTests`), E2E `Player/CarouselTests`, `ClockTests` (Playwright Clock API),
  `SlideTypeTests` (testmedia).
- Done: server (7cee47a), player (ad4b840), clock E2E, Image/WebPage E2E, ADR-0008 NITs, ARCHITECTURE §4/§5/§6.
- Self-check green: build 0 warnings, `dotnet test` 417 passed, `scripts/test-e2e.sh` 33 (8 opt-in spike skips).
- Known flake (not WP-08 code): `dotnet test` on the host twice crashed the E2E module at start with "Internal CLR
  error (0x80131506)" before any test ran (all its tests skip on the host); reruns pass and the module alone passed
  6/6. If it recurs, capture a dump (`DOTNET_DbgEnableMiniDump=1`) and file it.
- Round 1: CHANGES_REQUIRED (4 MAJOR, 4 MINOR, 3 NIT). Fixed in code: shadow only while animating; carousel loop
  (no second dwell after a replaced/skipped/expired slide, empty state + 10 s retry when nothing can be shown);
  cached playback before the token exchange; reload no longer stuck; seed under advisory lock; weak ETag; preview 404
  message; pairing tests (race interceptor, earlier code). E2E tests added for every finding (each fails on the
  pre-fix player, checked); PLAYER_SPEC §2/§9/§10 updated.
- Self-check green: build 0 warnings, `dotnet test` 428 passed, E2E 38 (8 opt-in spike skips).
- Next step: handle the review round 2 verdict (docs/reviews/WP-08-review-2.md).
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

## Open questions for the product owner
- (answered) WP-02 round 4 approved by the product owner on 2026-10-17.
- (answered) WP-07 daily player reload confirmed by the product owner on 2026-10-17 (ADR-0008).
- (answered) WP-07: the PO confirmed the daily reload stands with the corrected ~1.3 GB/day worst case and approved review round 4 (2026-10-17).

## Notes
- The shell is zsh and `grep` is aliased to ugrep: use `command grep` in scripts. Quote `-?`.
- xUnit v3 on MTP: filters are `--filter-class/--filter-method/--filter-trait`; exit code 8 = an assembly matched no tests.
- E2E: the app is `http://signage-web:8080` inside Docker (Chromium forces HTTPS for host `app`). E2E runner args are xUnit runner args (`-trait "Category=X"`).
- The user `hello-world` container `keen_nobel` is the product owner's, not ours; leave it.
