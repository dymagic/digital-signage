# Progress & hand-off notes

> Written by the developer for its future self (context resets between sessions). The reviewer
> does not read this file.

## Status
Milestone 1: in progress (WP-00, WP-01 done). Project docs, skills and the reviewer agent were set up on 2026-10-16.

## Current package
- Package: WP-02 Authentication & users — branch `wp-02-auth` — status In review (round 3 requested)
- Plan: `Features/Users` (UserAccounts service: create/verify+lockout/change/reset/role/active with
  last-admin guard; PasswordHasher<User> + own lockout on TimeProvider — ADR-0004 deviates from
  AddIdentityCore), cookie auth (`signage.auth`, HttpOnly, SameSite=Lax, Secure=SameAsRequest),
  per-request principal validation against DB (security stamp, active, role, must-change),
  policies RequireAdmin/RequireEditor, must-change page filter, first-run Setup (advisory lock),
  bootstrap env vars in DatabaseInitializer path, Settings page (admin; default duration, overlay,
  time zone) because no other package owns it. Razor Pages under /admin, lowercase URLs, layout shell,
  admin.css, SharedResource.resx + IStringLocalizer<SharedResource> with keys + unit test that every
  L["key"] used exists. E2E compose bootstraps an admin; E2E login + forced-change smoke.
- Risks: htmx antiforgery header later; Secure cookie flag vs plain-HTTP LAN deployments (ADR).
- Done: UserAccounts (lockout on TimeProvider, first admin w/ advisory lock, create/reset/update with last-admin + self guards), cookie auth with per-request DB validation, policies, must-change filter, bootstrap, SettingsService, pages (login, logout, setup, forbidden, home, password, users list/create/edit, settings, error), layout + admin.css, SharedResource.resx (80 keys). Builds clean.
- Half-done: —
- Done (2): 70+ integration tests for role matrix, sign-in/lockout/cookie flags, setup (+ concurrent), session invalidation, user admin, bootstrap, settings — all green.
- Done (3): resx coverage + password policy unit tests; E2E admin (bootstrap + forced change, shared storage state); fixed atomic lockout + duplicate antiforgery token; ADR-0004. Self-check: build clean, 153 tests green, E2E 3x green.
- Round 1: CHANGES_REQUIRED (2 MAJOR lockout race + editor POST tests, 4 MINOR, 2 NIT); all fixed in 1a13703 (NIT 8 noted under WP-04).
- Round 2: CHANGES_REQUIRED (2 BLOCKER: stale hash write-back, xmin bumps by sign-ins; 1 MINOR; 1 NIT); fixed in 10dfd46. Round 3 is the last before escalation.
- Next step: handle review round 3 verdict (docs/reviews/WP-02-review-3.md).
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

## Open questions for the product owner
_(none)_

## Notes
- The shell is zsh and `grep` is aliased to ugrep: use `command grep` in scripts. Quote `-?`.
- xUnit v3 on MTP: filters are `--filter-class/--filter-method/--filter-trait`; exit code 8 = an assembly matched no tests.
- E2E: the app is `http://signage-web:8080` inside Docker (Chromium forces HTTPS for host `app`). E2E runner args are xUnit runner args (`-trait "Category=X"`).
- The user `hello-world` container `keen_nobel` is the product owner's, not ours; leave it.
