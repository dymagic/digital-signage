# Review WP-00 — round 1
- Branch: wp-00-foundation @ a6a0aeb
- Verdict: APPROVE
- Verified:
  - `dotnet build`: 0 warnings, 0 errors. `dotnet build --no-incremental`: 0 warnings, 0 errors (TreatWarningsAsErrors + latest-recommended analyzers are on).
  - `dotnet test`: 6 total, 5 passed, 1 skipped (the E2E smoke test skips on the host by design), 0 failed. The integration tests ran against a real `postgres:16` Testcontainer.
  - `scripts/test-e2e.sh`: `Signage.E2E Total: 1, Errors: 0, Failed: 0, Skipped: 0`. The smoke test really ran in `mcr.microsoft.com/playwright/dotnet:v1.63.0-noble`, matching `Microsoft.Playwright` 1.63.0. `artifacts/e2e/results.xml` is owned by the host user.
  - `docker compose up -d --build`: `/healthz` returned 200 "Healthy" and `/readyz` returned 200. `/lib/htmx/htmx.min.js` returned 200. The container runs as uid 1654 (non-root).
  - `docker compose stop db`: `/readyz` returned 503 and `/healthz` stayed 200. After `start db`, `/readyz` returned 200 again with no app restart. Afterwards I ran `docker compose down -v`.
  - `docker buildx build --platform linux/arm64 .`: succeeded (the build stage is native and the publish is cross-arch). I deleted the image afterwards.
  - The htmx file's SHA-256 matches `wwwroot/lib/README.md` (`d6fdc75f…f717`), and the embedded version string is `2.0.11`.
  - No tracked file is larger than 1 MB. No secrets beyond the labelled demo/dev DB passwords.

## Findings
| # | Severity | Location | Finding | Expected |
|---|---|---|---|---|
| 1 | MINOR | `src/Signage.Web/appsettings.Development.json:3`, `docker-compose.yml:7-13`, `.claude/skills/run-and-verify/SKILL.md:20-21` | The documented inner loop (`dotnet watch` on the host against `docker compose up -d db`) cannot work. The compose `db` publishes no port, and the dev connection string uses password `signage-dev-only` while compose sets `signage-demo-password`. | Make them consistent. For example, publish `5432` in a dev-only override or document one, and align the dev password, or change the skill text. |
| 2 | MINOR | `CLAUDE.md:50,56` | The header now says "verified in WP-00", but `dotnet ef migrations add … # via local tool manifest` has no tool manifest yet (`.config/dotnet-tools.json` is missing). | Either add the manifest when EF lands in WP-01 or don't claim the whole block is verified. Keep docs truthful. |
| 3 | MINOR | `tests/Signage.E2E/Infrastructure/E2EEnvironment.cs:12`, `tests/Signage.E2E/Smoke/HealthSmokeTests.cs:12-20` | `ArtifactsDir` is never used (dead code). The run-and-verify skill promises traces and screenshots on failure, but nothing produces them. The page is also not disposed when an assertion fails, because `CloseAsync` runs after the asserts. | Before the player E2E work (WP-08), add a per-test context with tracing/screenshot on failure that writes to `ArtifactsDir`, plus `await using` disposal. Otherwise remove the property and the claim. |
| 4 | NIT | `tests/Signage.E2E/Infrastructure/BrowserFixture.cs:42,58` | Uses `DateTimeOffset.UtcNow` for the wait deadline. CLAUDE.md says never to use `UtcNow`. This is test infrastructure measuring elapsed time, so the impact is nil. | Use `Stopwatch` (or `TimeProvider.System`). |
| 5 | NIT | `src/Signage.Web/Program.cs:24-25` | Detecting an explicit GSS setting by substring is fragile. It misses spelling variants that Npgsql also accepts, for example `GSS Encryption Mode` with different spacing. | Parse with `NpgsqlConnectionStringBuilder` and check whether the key is present, or document the limitation. |
| 6 | NIT | `.claude/skills/run-and-verify/SKILL.md:12` | `docker compose ps   # app and db healthy?`: the app has no container healthcheck (ADR-0002), so it will never show "healthy". | Reword it to say "db healthy, app running". |

## Requirement check
| R-ID / criterion | Met? | Evidence (test name or file:line) |
|---|---|---|
| git hygiene, `.gitignore`, `.gitattributes`, `.editorconfig` | Yes | `.gitignore` ignores bin/obj/artifacts/overrides/.env. `.gitattributes` forces LF. `.editorconfig` sets file-scoped namespaces as a warning. |
| `Directory.Build.props` (nullable, warnings-as-errors, analyzers) | Yes | `Directory.Build.props:5-9`. Central package management in `Directory.Packages.props` (ADR-0002). |
| `Signage.sln` + `Signage.Web` skeleton with `/healthz` + `/readyz` | Yes | `HealthEndpoints.cs:22-23`. `HealthEndpointTests.Healthz_returns_200`, `Readyz_returns_200_when_database_is_reachable`, `Readyz_returns_503_but_healthz_stays_200_when_database_is_unreachable` |
| Three test projects | Yes | `tests/Signage.UnitTests`, `Signage.IntegrationTests` (Testcontainers assembly fixture), `Signage.E2E` |
| Multi-stage Dockerfile, non-root, amd64+arm64 | Yes | `Dockerfile:5,198-201` (chiseled-extra, `USER $APP_UID`). uid 1654 observed at runtime, and the arm64 buildx build succeeded. |
| `docker-compose.yml` app + postgres:16 with healthcheck | Yes | `docker-compose.yml:8-20`, app `depends_on: service_healthy` |
| `docker-compose.e2e.yml` with the Playwright image version matching the package | Yes | `scripts/test-e2e.sh:7` reads the version from `Directory.Packages.props:170`, and `tests/Signage.E2E/Dockerfile:4` uses it |
| One smoke E2E test loading `/healthz` + `scripts/test-e2e.sh` | Yes | `HealthSmokeTests.Healthz_loads_in_the_browser`. The script passes args through and propagates the exit code. |
| Vendor htmx into `wwwroot/lib` | Yes | `wwwroot/lib/htmx/htmx.min.js` 2.0.11 + LICENSE (0BSD) + README with the hash |
| Accept: `dotnet build` clean | Yes | 0 warnings (clean rebuild) |
| Accept: `dotnet test` green (Testcontainers) | Yes | 5 passed / 1 skipped (E2E on host) |
| Accept: `docker compose up` serves `/healthz` 200 | Yes | Verified with curl |
| Accept: `scripts/test-e2e.sh` passes | Yes | 1/1 passed |
| R-60 (partly): container, non-root, env config, healthz/readyz | Partly, as scoped | Migrations-on-startup belongs to WP-01. Config comes from env (`ConnectionStrings__Default`), and a missing value fails fast: `Startup_fails_with_a_clear_message_when_the_connection_string_is_missing`. |
| R-61 (partly): compose gives app + Postgres at :8080 | Partly, as scoped | Verified. A "working system" needs later packages. |
| Decision recorded | Yes | `docs/adr/0002-build-test-and-container-tooling.md` (xUnit v3/MTP, chiseled-extra, no HEALTHCHECK, signage-web alias). The reasoning holds. |
| No out-of-scope additions / glossary violations | Yes | None found |

## Notes for the product owner
- The branch also changes `.claude/settings.json` (commit a6a0aeb). It removes the deny rules that stopped *any* agent, including the developer, from editing `docs/reviews/*-review-*.md`. The commit says you asked for this so the reviewer can write its file. That is plausible, but I can't verify it. Since that change, "never edit reviews" is enforced only by the CLAUDE.md instruction. If you want a hard guard back, consider a scoped rule (for example, deny in the main session and allow in the reviewer agent's own definition). Alternatively, check `git log -- docs/reviews/` at milestone time to confirm that only reviewer-authored commits touch those files.
- At runtime, the app logs data-protection warnings (keys stored in the container filesystem, unencrypted). That is expected at this stage. WP-01 is scheduled to store the keys in the database (R-63).
