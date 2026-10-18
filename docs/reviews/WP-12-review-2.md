# Review WP-12 — round 2
- Branch: wp-12-packaging @ f01126a
- Verdict: APPROVE
- Verified:
  - `dotnet build`: Build succeeded, 0 warnings, 0 errors.
  - `dotnet test`: 474 total, 412 passed, 62 skipped (E2E without the Docker runner), 0 failed.
  - Round 2 changes are documentation only (`git show f01126a --stat`: README, CONTRIBUTING, both deploy guides, PROGRESS). No code, test or player change since round 1, so I didn't re-run `scripts/test-e2e.sh` (62 total, 0 failed in round 1 at 9e68040, and the tests haven't changed since).
  - **Manual migration path (README "To migrate by hand"), run literally:** `dotnet tool restore` then `dotnet ef migrations script --idempotent -p src/Signage.Web -o migrate.sql` builds and writes a 422-line script without a database. I applied it with `psql -v ON_ERROR_STOP=1` to a fresh `postgres:17` container: exit 0, 4 rows in `__EFMigrationsHistory` (matches the 4 migrations). A second run is a no-op, so the script really is idempotent. I then started the app with `SIGNAGE__MIGRATEONSTARTUP=false` against that database: it started (no pending-migrations refusal), `/readyz` returned 200 `Healthy` and `/admin` redirected to login. Container and process removed afterwards.
  - Azure build step: `az acr login` plus `docker buildx build --platform linux/amd64 … --push .` is the standard ACR push path, and round 1 showed the Dockerfile builds under buildx for amd64. A single-platform `--push` works on the default docker driver.
  - Password minimum: `PasswordPolicy.MinLength` (used by `AdminBootstrapper.cs:43` and the setup/password hints) agrees with the "at least 10 characters" now in both guides.
  - All 7 committed screenshots are now referenced (README.md:7, 23-25).

## Findings
| # | Severity | Location | Finding | Expected |
|---|---|---|---|---|
| 1 | NIT | README.md:113-115 | The manual migration snippet assumes `psql` on the operator's machine. If the image they deploy is older or newer than their checkout, the script won't match. The text already says to use "the **same version**", which covers the second point. | Optional: note that `docker run --rm -i postgres psql …  < migrate.sql` works without a local client. |
| 2 | NIT | tests/Signage.E2E/Docs/ReadmeScreenshots.cs:55 | Carried over from round 1, NIT 9: a fixed 4 s wait frames the progress bar. It's acceptable in an opt-in docs generator and has a comment. | No action needed. |

## Requirement check
| R-ID / criterion | Met? | Evidence (test name or file:line) |
|---|---|---|
| R-60 Container (multi-arch, non-root, env config, health endpoints, migrations switchable) | Yes | Dockerfile (chiseled, `USER $APP_UID`); round-1 buildx amd64+arm64 build; README config table; switch-off path now documented and verified by hand (above) |
| R-61 docker compose gives a working system at :8080 | Yes | Round-1 fresh-clone quick start; nothing affecting it changed in round 2 apart from wording on the DB password (README.md:51-54, accurate) |
| R-62 Cloud docs (ACA + Azure PG; AWS ECS/App Runner + RDS; no IaC) | Yes (docs; not run against real accounts) | docs/deploy/azure-container-apps.md:46-57, 121-126 now use buildx + push; docs/deploy/aws.md unchanged apart from the password rule |
| R-65 OSS hygiene | Yes | LICENSE (MIT); README; CONTRIBUTING; screenshots all used and < 100 KB |
| R-07 kiosk autoplay flags documented | Yes | README "Setting up a screen" section (Chromium and Firefox flags) |
| Accept: quick start on a clean clone works | Yes | Round 1 (verified by hand) |
| Multi-arch build instructions | Yes | README.md:150-153 including the `docker buildx create --use` fallback |
| Demo-data instructions | Yes | README quick start and `SIGNAGE__SEEDDEMO` row |
| LICENSE check | Yes | LICENSE; wwwroot/lib/htmx/LICENSE |
| Glossary / scope | Yes | No new terms or out-of-scope content in the round-2 diff |

## Previous round follow-up
| Round-1 # | Severity | Status | Evidence |
|---|---|---|---|
| 1 | MAJOR | Fixed | Azure guide steps 3 and "Updating" use `az acr login` + `docker buildx build --platform linux/amd64 … --push`, with a note on why `az acr build` isn't used |
| 2 | MINOR | Fixed | README.md:95 and 110-116: refusal behaviour explained, idempotent-script procedure given; verified end to end |
| 3 | MINOR | Fixed | README.md:25 now shows `admin-slides.jpg` and `player-pairing.jpg` |
| 4 | NIT | Fixed | CONTRIBUTING.md:25-31: port 5080 stated, redundant variable dropped (`appsettings.Development.json` explained) |
| 5 | NIT | Fixed | README.md:63 "a link that is shown only once" |
| 6 | NIT | Fixed | Both guides say "at least 10 characters" |
| 7 | NIT | Fixed | README.md:51-54: both places, "before the first start", and why |
| 8 | NIT | Fixed | README.md:153 `docker buildx create --use` hint |
| 9 | NIT | Left as is (acceptable) | See finding 2 |

## Notes for the product owner
- Neither cloud guide has been run against a real Azure or AWS account (the reviewer can't). Do that once during milestone acceptance before treating R-62 as proven in practice.
- Round 1's suggestion still stands: a README FAQ line on the "No XML encryptor configured" start-up log would save questions from people trying the app.
