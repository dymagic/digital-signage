# Progress & hand-off notes

> Written by the developer for its future self (context resets between sessions). The reviewer
> does not read this file.

## Status
Milestone 1: in progress (WP-00 done). Project docs, skills and the reviewer agent were set up on 2026-10-16.

## Current package
- Package: WP-01 Data model & settings — branch `wp-01-data-model` — status In progress
- Plan: entities in `Domain/` (Guid v7 ids, enums as strings, DateTimeOffset UTC, xmin concurrency on
  human-edited roots, case-insensitive ICU collation on unique names, NormalizedEmail);
  `Data/SignageDbContext` + `Configurations/`, design-time factory, `InitialCreate` migration;
  `Data/DatabaseInitializer` (migrate if `Signage:MigrateOnStartup`, ensure SystemSettings row via
  ON CONFLICT); data-protection keys in DB; `TimeProvider.System` singleton. Pure `SequenceResolver`
  + `SlideSchedule` (half-open [from, until)) + `SlideDuration` with exhaustive unit tests;
  `ScreenGraphLoader` + integration test (resolve from real DB). Integration: per-test databases.
  Plus WP-00 follow-ups (a)-(f) from BACKLOG.
- Risks: concurrent migrations across replicas (EF 9+ migration lock); dense positions without a unique
  index (swap updates would violate it).
- Done: packages + dotnet-ef manifest (`dotnet-tools.json` at repo root); domain entities; SequenceResolver, SlideSchedule, SlideDuration with 41 green unit tests.
- Half-done: —
- Next step: SignageDbContext + configurations, design-time factory, InitialCreate migration.
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

## Open questions for the product owner
_(none)_

## Notes
- The shell is zsh and `grep` is aliased to ugrep: use `command grep` in scripts. Quote `-?`.
- xUnit v3 on MTP: filters are `--filter-class/--filter-method/--filter-trait`; exit code 8 = an assembly matched no tests.
- E2E: the app is `http://signage-web:8080` inside Docker (Chromium forces HTTPS for host `app`). E2E runner args are xUnit runner args (`-trait "Category=X"`).
- The user `hello-world` container `keen_nobel` is the product owner's, not ours; leave it.
