# Progress & hand-off notes

> Written by the developer for its future self (context resets between sessions). The reviewer
> does not read this file.

## Status
Milestone 1: in progress (WP-00–WP-02 done). Project docs, skills and the reviewer agent were set up on 2026-10-16.

## Current package
- Package: WP-03 Slide library — branch `wp-03-slides` — status In review (round 1 requested)
- Plan: `Features/Slides`: `SlideInput` + pure `SlideValidator` (absolute http(s) URL, required field per
  type, duration 1–3600 not for video, play-audio video only, until > from, .mp4 warning only),
  `ScheduleClock` (system-TZ local ↔ UTC, DST gap = error, ambiguous = standard time), `SlideService`
  (list + ILIKE search, create, update with xmin conflict, enable toggle, usage counts, delete removing
  links). Pages `/admin/slides` (list, thumbnails, search), `/admin/slides/create?type=`, `/admin/slides/{id}`
  (edit + preview), `/admin/slides/{id}/delete` (confirm with usage). Type is fixed after creation.
  Nav "Slides" for editors. Unit tests: validator, schedule clock. Integration: CRUD, validation, TZ
  conversion, role matrix rows, delete-in-use, conflict, XSS encoding, sandbox attrs. WP-01 follow-ups
  (ExecuteUpdate note, UpdatedAt test).
- Risks: datetime-local parsing/culture; preview iframes must never get allow-same-origin for Html.
- Done: SlideValidator + ScheduleClock (unit-tested), SlideService, pages (list with previews/search/toggle, create, edit + preview, delete with usage), nav, ~75 strings, CSS.
- Half-done: —
- Done (2): integration + E2E tests, WP-01 follow-ups, ADR-0005. Self-check: build clean, 246 tests green, E2E 7/7.
- Next step: handle review round 1 verdict (docs/reviews/WP-03-review-1.md).
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

## Open questions for the product owner
- (answered) WP-02 round 4 approved by the product owner on 2026-10-17.

## Notes
- The shell is zsh and `grep` is aliased to ugrep: use `command grep` in scripts. Quote `-?`.
- xUnit v3 on MTP: filters are `--filter-class/--filter-method/--filter-trait`; exit code 8 = an assembly matched no tests.
- E2E: the app is `http://signage-web:8080` inside Docker (Chromium forces HTTPS for host `app`). E2E runner args are xUnit runner args (`-trait "Category=X"`).
- The user `hello-world` container `keen_nobel` is the product owner's, not ours; leave it.
