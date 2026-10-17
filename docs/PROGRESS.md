# Progress & hand-off notes

> Written by the developer for its future self (context resets between sessions). The reviewer
> does not read this file.

## Status
Milestone 1: in progress (WP-00–WP-06 done). Project docs, skills and the reviewer agent were set up on 2026-10-16.

## Current package
- Package: WP-07 Spike: video lifecycle & memory — branch `wp-07-video-spike` — status In review (round 1 requested)
- Plan: generate H.264 test media in a container (`scripts/generate-test-media.sh`, linuxserver/ffmpeg:9.0.2), serve it
  and the harness from nginx (`testmedia` service in docker-compose.e2e.yml), harness in
  `tests/Signage.E2E/spikes/video-lifecycle/` with 4 strategies (release / remove / keep / reuse), runner
  `tests/Signage.E2E/Spikes/VideoLifecycleSpike.cs` (240 transitions, samples every 20: JS heap after GC, nodes,
  browser PSS from /proc, videos in DOM, media players created/destroyed via CDP Media). Then ADR-0008 with data.
- Done: media, harness, runner, measurements (240 x 4 strategies; 1200 release vs images-only), ADR-0008, PLAYER_SPEC §4.
- Next step: handle review round 1 verdict (docs/reviews/WP-07-review-1.md).
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

## Open questions for the product owner
- (answered) WP-02 round 4 approved by the product owner on 2026-10-17.

## Notes
- The shell is zsh and `grep` is aliased to ugrep: use `command grep` in scripts. Quote `-?`.
- xUnit v3 on MTP: filters are `--filter-class/--filter-method/--filter-trait`; exit code 8 = an assembly matched no tests.
- E2E: the app is `http://signage-web:8080` inside Docker (Chromium forces HTTPS for host `app`). E2E runner args are xUnit runner args (`-trait "Category=X"`).
- The user `hello-world` container `keen_nobel` is the product owner's, not ours; leave it.
