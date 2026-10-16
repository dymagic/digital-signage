# Progress & hand-off notes

> Written by the developer for its future self (context resets between sessions). The reviewer
> does not read this file.

## Status
Milestone 1: not started. Project docs, skills and the reviewer agent were set up on 2026-10-16.

## Current package
- Package: WP-00 Repository foundation — branch `wp-00-foundation` — status In review (round 1 requested)
- Plan: `Signage.sln` (classic sln format, as documented), `Directory.Build.props` + central package
  management (`Directory.Packages.props`), `.editorconfig`; `src/Signage.Web` (Razor Pages skeleton,
  `/healthz` liveness, `/readyz` Postgres check via `NpgsqlDataSource`); `tests/Signage.UnitTests`,
  `tests/Signage.IntegrationTests` (WebApplicationFactory + Testcontainers postgres:16),
  `tests/Signage.E2E` (Playwright .NET, skipped unless `SIGNAGE_E2E_BASEURL` is set); xUnit v3.
  Dockerfile (sdk build on $BUILDPLATFORM, `-a $TARGETARCH`, runtime `aspnet:10.0-noble-chiseled-extra`
  for ICU/tzdata, non-root). compose: app + postgres:16. `docker-compose.e2e.yml` + `tests/Signage.E2E/Dockerfile`
  FROM `mcr.microsoft.com/playwright/dotnet:v<ver>-noble`, version read from Directory.Packages.props by
  `scripts/test-e2e.sh` so image and package cannot drift. htmx 2.0.11 vendored.
- Risks: chiseled image has no shell/curl → no in-image HEALTHCHECK for the app (orchestrators probe HTTP).
- Done: everything in the plan. build clean; dotnet test green (5 passed, E2E skipped on host); scripts/test-e2e.sh green; compose serves /healthz + /readyz; arm64 buildx build verified (no emulation).
- Half-done: —
- Next step: handle review round 1 verdict (docs/reviews/WP-00-review-1.md).
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

## Open questions for the product owner
_(none)_

## Notes
- The shell is zsh and `grep` is aliased to ugrep: use `command grep` in scripts. Quote `-?`.
- xUnit v3 on MTP: filters are `--filter-class/--filter-method/--filter-trait`; exit code 8 = an assembly matched no tests.
- E2E: the app is `http://signage-web:8080` inside Docker (Chromium forces HTTPS for host `app`). E2E runner args are xUnit runner args (`-trait "Category=X"`).
- The user `hello-world` container `keen_nobel` is the product owner's, not ours; leave it.
