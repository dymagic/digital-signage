# Progress & hand-off notes

> Written by the developer for its future self (context resets between sessions). The reviewer
> does not read this file.

## Status
Milestone 1: in progress (WP-00–WP-02 done). Project docs, skills and the reviewer agent were set up on 2026-10-16.

## Current package
_None. Next: WP-03 Slide library._

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
