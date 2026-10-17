# Progress & hand-off notes

> Written by the developer for its future self (context resets between sessions). The reviewer
> does not read this file.

## Status
Milestone 1: in progress (WP-00–WP-03 done). Project docs, skills and the reviewer agent were set up on 2026-10-16.

## Current package
- Package: WP-04 Playlists — branch `wp-04-playlists` — status In progress
- Plan: `Data/PositionMaintenance` (SQL ROW_NUMBER renumbering for playlist_slides, screen_slides per
  placement, screen_playlists, group_playlists); `Features/Playlists/PlaylistService` (list with counts,
  get with entries, create/update with unique-name + xmin, add/remove/move entry = dense rewrite + playlist
  version bump, usage, delete + renumber affected screens/groups); SlideService.DeleteAsync renumbers too.
  Pages `/admin/playlists` (list), `create`, `{id}` (details form + entries partial `_PlaylistEntries` driven
  by htmx page handlers Add/Remove/Move with version; without JS the same forms post and redirect),
  `{id}/delete`. Drag & drop in `wwwroot/admin/reorder.js` (native HTML5 DnD, no library) plus up/down
  buttons. Handlers are Razor page handlers, so MustChangePasswordFilter applies (WP-02 follow-up).
  WP-03 follow-ups: dense renumber on slide delete, E2E wait fix, NITs 5–9.
- Risks: htmx antiforgery header; drag & drop in Playwright (use DragToAsync).
- Done: —
- Half-done: —
- Next step: PositionMaintenance + PlaylistService with integration tests.
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

## Open questions for the product owner
- (answered) WP-02 round 4 approved by the product owner on 2026-10-17.

## Notes
- The shell is zsh and `grep` is aliased to ugrep: use `command grep` in scripts. Quote `-?`.
- xUnit v3 on MTP: filters are `--filter-class/--filter-method/--filter-trait`; exit code 8 = an assembly matched no tests.
- E2E: the app is `http://signage-web:8080` inside Docker (Chromium forces HTTPS for host `app`). E2E runner args are xUnit runner args (`-trait "Category=X"`).
- The user `hello-world` container `keen_nobel` is the product owner's, not ours; leave it.
