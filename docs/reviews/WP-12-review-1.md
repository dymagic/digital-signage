# Review WP-12 — round 1
- Branch: wp-12-packaging @ 9e68040
- Verdict: CHANGES_REQUIRED
- Verified:
  - `dotnet build`: Build succeeded, 0 warnings, 0 errors.
  - `dotnet test`: 474 total, 412 passed, 62 skipped (E2E without the Docker runner), 0 failed.
  - **Quick start, run literally on a fresh clone** (`git clone -b wp-12-packaging … signage && cd signage && docker compose up -d --build`, with no `signage_db-data` volume beforehand): migrations applied, demo seeded ("Demo content created…"), `/healthz` 200, `/readyz` 200, `/` → `/admin` → `/admin/setup`. I created the first admin through the setup form, saw "Welcome (demo)" under Playlists, started a pairing (`POST /api/player/pairing` → code `394-V9D`), paired it in **Screens → Pair screen → An existing screen → Demo screen** (302 to the screen page), and the device poll returned `paired` with the cookie. `GET /api/player/manifest` then returned the Demo screen's 5-slide demo playlist. Both NASA image URLs return 200 image/jpeg. Then `docker compose down -v`.
  - CONTRIBUTING's run-from-source loop: `docker compose up -d db` plus `ConnectionStrings__Default=… dotnet run --project src/Signage.Web` serves `/readyz` 200 on `http://localhost:5080`.
  - `scripts/test-e2e.sh` (fresh clone): 62 total, 0 failed, 9 skipped (spikes and the opt-in README screenshots).
  - `scripts/update-readme-screenshots.sh` (fresh clone): 1 passed, and it writes the 7 JPEGs to `docs/images/` (40–90 KB each). I discarded that output afterwards.
  - `docker buildx build --platform linux/amd64,linux/arm64 -o type=cacheonly .`: both platforms build (containerd image store).
  - `DOCKER_BUILDKIT=0 docker build .` (legacy builder): **fails** at step 1 with `failed to parse platform : "" is an invalid OS component` (see finding 1).
  - I spot-checked the config table against `SignageOptions.cs`, `ForwardedHeadersSetup.cs`, `AdminBootstrapper.cs`, `DemoSeeder.cs` and `DatabaseInitializer.cs`. I checked the UI labels the README names against `SharedResource.resx` and the player layer order against the `player.css` z-indexes. All match.

## Findings
| # | Severity | Location | Finding | Expected |
|---|---|---|---|---|
| 1 | MAJOR | docs/deploy/azure-container-apps.md:52, 120 | The Azure guide builds the image with `az acr build` (step 3 and "Updating"). The Dockerfile depends on BuildKit: `FROM --platform=$BUILDPLATFORM` and `-a $TARGETARCH` (Dockerfile:5,7,12,15). Without BuildKit, those automatic platform args are empty and the build fails on its first line, which I reproduced with `DOCKER_BUILDKIT=0`. ACR Tasks have historically run the classic builder for `az acr build`, and nothing shows this path was tested. The guide's main build path may therefore not work, which undercuts R-62. | Either show that `az acr build` works with this Dockerfile (cite the ACR BuildKit support or record a real run), or make the guide's main path `az acr login -n $ACR` plus `docker buildx build --platform linux/amd64 -t $ACR.azurecr.io/signage:0.1.0 --push .`. A third option is `az acr build` with explicit `--build-arg TARGETARCH=amd64` and a Dockerfile that tolerates a missing `BUILDPLATFORM`, tested with `DOCKER_BUILDKIT=0`. |
| 2 | MINOR | README.md:92 (config table, `SIGNAGE__MIGRATEONSTARTUP`) | "Set `false` to migrate yourself", but nothing says how. The image ships no migration bundle or SQL, and `DatabaseInitializer.cs:66-69` refuses to start while migrations are pending. An operator who turns this off (R-60 "can be switched off") has no documented way forward. | One line showing how, e.g. `dotnet ef migrations script --idempotent -p src/Signage.Web -o migrate.sql` from a checkout of the same version, then apply it with `psql`. |
| 3 | MINOR | docs/images/admin-slides.jpg, docs/images/player-pairing.jpg; tests/Signage.E2E/Docs/ReadmeScreenshots.cs:46,77 | Two of the seven committed screenshots aren't referenced by any document. The binaries are committed without being used, and the generator keeps producing them. | Use them (pairing is a natural fit for "Setting up a screen", the slide library for the feature list), or stop taking them and delete the files. |
| 4 | NIT | CONTRIBUTING.md:25-31 | The run-from-source section doesn't say where the app is served (`http://localhost:5080`, from launchSettings). The `ConnectionStrings__Default` prefix is also redundant, because `appsettings.Development.json` already holds the same string. | Mention the port. Optionally drop the redundant variable, or say why it's there. |
| 5 | NIT | README.md:60 | "gives a one-time link". The link is *shown* once (ADR-0007), but it stays valid and is reused after a browser wipe, as the next sentence says. "One-time" suggests single use. | "a link that is shown only once". |
| 6 | NIT | docs/deploy/azure-container-apps.md:22, docs/deploy/aws.md:52 | Both guides say the bootstrap password needs "at least 12 characters", but `PasswordPolicy.MinLength` is 10. | Use the real rule, or say 12 is a recommendation. |
| 7 | NIT | README.md:50-51 | The password advice doesn't say the password lives in two places (`POSTGRES_PASSWORD` and the app's connection string), or that changing it after the first start doesn't change the existing volume's password. | Mention both places and "before the first start". |
| 8 | NIT | README.md:139 | The multi-arch `buildx … --push` command needs the containerd image store or a `docker-container` builder. On the classic store it fails with "Multi-platform build is not supported for the docker driver". | Add "(if Docker says multi-platform isn't supported: `docker buildx create --use`)". |
| 9 | NIT | tests/Signage.E2E/Docs/ReadmeScreenshots.cs:55 | `WaitForTimeoutAsync(4000)` is a fixed sleep. It's acceptable here because it only frames the progress bar in an opt-in docs generator, not an assertion. | Optional: wait on a progress-bar state instead, or leave a comment saying it's deliberate (one is there already). |

## Requirement check
| R-ID / criterion | Met? | Evidence (test name or file:line) |
|---|---|---|
| R-60 Container (multi-arch, non-root, env config, /healthz, /readyz, migrations switchable) | Yes (code from earlier WPs); docs partly (finding 2) | Dockerfile:5-27 (chiseled, `USER $APP_UID`); buildx amd64+arm64 build succeeded; `/healthz` and `/readyz` 200 on the quick-start stack; README.md:85-108 config table matches `SignageOptions.cs` |
| R-61 docker compose gives a working system at :8080 | Yes | Fresh-clone quick start run above: setup, demo playlist, pairing to "Demo screen", manifest served |
| R-62 Cloud docs (ACA + Azure PG; AWS ECS/App Runner + RDS; no IaC) | Partly | docs/deploy/azure-container-apps.md and docs/deploy/aws.md are complete and accurate on LISTEN/NOTIFY (no PgBouncer or RDS Proxy), min replicas ≥ 1, forwarded headers, SSE keep-alive and TLS. The Azure build step is unverified and likely broken (finding 1) |
| R-65 OSS hygiene (MIT LICENSE, README with what/screenshots/quick start/security/no-warranty, CONTRIBUTING, clean .gitignore) | Yes | LICENSE (MIT, contributors); README.md:1-153; CONTRIBUTING.md; .gitignore covers bin/obj/artifacts/overrides/.env; screenshots < 100 KB each, made by `ReadmeScreenshots` with example.org data only |
| R-07 kiosk autoplay flags documented | Yes | README.md:64-83 (Chromium `--autoplay-policy=no-user-gesture-required`, Firefox `media.autoplay.default=0` / policy) |
| Accept: quick start on a clean clone works | Yes | Verified by hand, see "Verified" |
| Multi-arch build instructions | Yes (see NIT 8) | README.md:137-140; Dockerfile:1-3 |
| Demo-data instructions | Yes | README.md:36-48; `SIGNAGE__SEEDDEMO` row; DemoSeeder runs on an empty database only |
| LICENSE check | Yes | LICENSE; wwwroot/lib/htmx/LICENSE plus lib/README.md |
| WP-01 follow-up: DB port 127.0.0.1:55432 / `SIGNAGE_DB_PORT` | Yes | README.md:103-105; CONTRIBUTING.md:25 |
| WP-02 follow-up: HTTPS via reverse proxy, no `Secure` over HTTP | Yes | README.md:119-121; `UsersServiceCollectionExtensions.cs:25` (`SameAsRequest`) agrees |
| WP-11 follow-ups: PLAYER_SPEC §1 layer order; ARCHITECTURE identify wording | Yes | PLAYER_SPEC.md:13-14 matches player.css z-indexes (slide 1/2, single video 10, progress 20, info 25, identify 30); ARCHITECTURE.md:125-126 |
| Glossary | Yes | No "monitor/card/feed" in new docs, except the rule itself in CONTRIBUTING.md:60 |
| Scope | Yes | No IaC; Pi images explicitly excluded (README.md:82-83) |

## Notes for the product owner
- I followed the quick start on a fresh clone and it works end to end. The demo needs internet only for the two NASA photos.
- I couldn't exercise either cloud guide against a real account. Finding 1 is the one concrete risk I found. Run each guide once against a real account before calling R-62 proven, perhaps during the milestone acceptance.
- The app logs "No XML encryptor configured" for data-protection keys at start-up. That's expected (keys in the database, not encrypted at rest), but people trying the app may ask about it. An FAQ line in the README would help.
