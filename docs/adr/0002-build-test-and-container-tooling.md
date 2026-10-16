# ADR-0002: Build, test and container tooling details

- Status: Accepted
- Date: 2026-10-16
- Work package: WP-00
- Requirements: R-60, R-61, R-65

## Context
WP-00 had to settle several tooling details that ARCHITECTURE.md leaves open: the xUnit
generation, how E2E tests run in Docker without drifting from the Playwright package, the runtime
base image, and how dependency versions are managed.

## Options considered
1. **xUnit v2 + VSTest** (what `dotnet new xunit` still generates): familiar, but v2 is in
   maintenance mode, and the .NET 10 SDK is moving `dotnet test` to Microsoft.Testing.Platform (MTP).
2. **xUnit v3 on MTP**: current and supported; it has assembly fixtures (one shared Postgres
   container) and dynamic skips. Each test project is an executable. Filters use the MTP/xUnit syntax
   (`--filter-class`, `-trait`) instead of VSTest's `--filter`.

Runtime image: `aspnet:10.0-noble` (has a shell, but larger, plus a manual non-root setup) vs.
`aspnet:10.0-noble-chiseled` (no shell, non-root by default, but no ICU/tzdata) vs.
`aspnet:10.0-noble-chiseled-extra` (chiseled + ICU + tzdata).

## Decision
- **xUnit v3 on Microsoft.Testing.Platform**, opted in via `global.json` (`"test": {"runner": ...}`).
  `dotnet test` runs unit + integration tests on the host. E2E tests skip themselves unless
  `SIGNAGE_E2E_BASEURL` is set.
- **E2E runner**: an image built `FROM mcr.microsoft.com/playwright/dotnet:v<version>-noble`.
  `scripts/test-e2e.sh` reads `<version>` from the `Microsoft.Playwright` entry in
  `Directory.Packages.props`, so the browsers and the package always match. The runner executes the
  test assembly directly (`dotnet Signage.E2E.dll`). `dotnet test` in the newer SDK inside that
  image misdetected the runner, and the direct call avoids that. Arguments to the script go to the
  xUnit runner. The compose project name is `signage-e2e`, so it never touches the dev stack.
- **Runtime image `aspnet:10.0-noble-chiseled-extra`**: non-root (uid 1654), no shell, with the
  tzdata/ICU that the IANA time-zone setting (R-03) needs. Since there is no shell or curl, the image
  has **no Docker `HEALTHCHECK`**. Orchestrators and operators probe `/healthz` and `/readyz` over
  HTTP, and the E2E fixture polls `/healthz` before starting.
- **Multi-arch** without emulation: the build stage runs on `$BUILDPLATFORM` and publishes with
  `-a $TARGETARCH`. The final stage has no `RUN`.
- **Central package management** (`Directory.Packages.props`) for every NuGet version.
- Npgsql's GSS-encryption probing is turned off by default (the chiseled image has no Kerberos
  libraries, so it logs an error on every connection). An explicit `Gss Encryption Mode` in the
  connection string still wins.

## Consequences
- Contributors need the .NET 10 SDK and Docker only. Filtering syntax differs from older
  tutorials, and the run-and-verify skill documents it.
- `dotnet test` with a filter returns exit code 8 when an assembly matches zero tests. That is
  expected and doesn't mean a failure.
- Docker `HEALTHCHECK` is unavailable. If an operator wants one, a future option is a tiny
  `--probe` mode in the app itself. Revisit if compose users ask for `healthy` status on the app.
- In E2E, the browser reaches the app as `http://signage-web:8080`, not `http://app:8080`: Chromium
  force-upgrades any `*.app` host to HTTPS (HSTS-preloaded TLD).
