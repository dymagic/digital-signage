# Progress & hand-off notes

> Written by the developer for its future self (context resets between sessions). The reviewer
> does not read this file.

## Status
Milestone 1: in progress (WP-00–WP-05 done). Project docs, skills and the reviewer agent were set up on 2026-10-16.

## Current package
- Package: WP-06 Pairing & screen tokens — branch `wp-06-pairing` — status In progress
- Plan: `Features/Pairing`: `PairingCode` (6 chars, unambiguous alphabet, shown XXX-XXX), `ScreenTokens`
  (256-bit random, SHA-256 hex at rest), `PairingService` (create request w/ device secret hash, unique among
  active codes, poll + one-time delivery that mints the token at delivery time so no plaintext is stored, claim
  for new or existing unpaired screen, expiry 15 min, cleanup), `ScreenAccess` (pre-configured URL token =
  new token shown once, revoke). Screen auth scheme `ScreenToken` (HttpOnly cookie `signage.screen`, path
  /api/player), minimal APIs `/api/player/pairing`, `/pairing/{id}`, `/session`, `/manifest` (minimal for now;
  WP-08 extends) with rate limits (per IP). Admin pages: `/admin/screens/pair`, screen detail admin actions
  (revoke, re-pair, create screen address). Player: `wwwroot/screen/` index.html + ES modules (config, api,
  strings, pairing, player) with pairing screen, token exchange from `?token=` (stripped), paired placeholder.
  ADR-0007: HttpOnly cookie instead of localStorage (R-40 wording), token minted at delivery, rate limits.
  Follow-up from WP-05: Name inputs nullable.
- Risks: rate limits are per replica; E2E needs timeScale for the revoke check.
- Done: PairingCode + ScreenTokens (unit-tested), PairingService (start/poll/claim/deliver), ScreenAccessService (address token, revoke, lookup), ScreenToken auth handler (cookie, 401 deletes it), PlayerApi (pairing, poll, session, minimal manifest), per-IP rate limits, AppInfo.
- Half-done: —
- Next step: admin pair page + screen detail actions (revoke, re-pair, screen address); player pairing UI.
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

## Open questions for the product owner
- (answered) WP-02 round 4 approved by the product owner on 2026-10-17.

## Notes
- The shell is zsh and `grep` is aliased to ugrep: use `command grep` in scripts. Quote `-?`.
- xUnit v3 on MTP: filters are `--filter-class/--filter-method/--filter-trait`; exit code 8 = an assembly matched no tests.
- E2E: the app is `http://signage-web:8080` inside Docker (Chromium forces HTTPS for host `app`). E2E runner args are xUnit runner args (`-trait "Category=X"`).
- The user `hello-world` container `keen_nobel` is the product owner's, not ours; leave it.
