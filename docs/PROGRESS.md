# Progress & hand-off notes

> Written by the developer for its future self (context resets between sessions). The reviewer
> does not read this file.

## Status
Milestone 1: not started. Project docs, skills and the reviewer agent were set up on 2026-10-16.

## Current package
- Package: WP-00 Repository foundation — branch `wp-00-foundation` — status In progress
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
- Done: solution, web skeleton with /healthz + /readyz, 3 test projects (xUnit v3 on MTP via global.json), htmx vendored; build clean, dotnet test green (E2E skips on host).
- Half-done: —
- Next step: Dockerfile, docker-compose.yml, docker-compose.e2e.yml, scripts/test-e2e.sh.
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
_(gotchas, environment quirks, anything a fresh session must know)_
