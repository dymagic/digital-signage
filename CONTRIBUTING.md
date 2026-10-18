# Contributing

Thanks for helping. Issues and pull requests are welcome; for anything bigger than a fix, open an issue first so we
can agree on the approach.

## What you need

- .NET 10 SDK (the exact version is pinned in `global.json`)
- Docker with Compose v2 (for PostgreSQL, the integration tests and the browser tests)

No Node.js, npm or Python: the player is plain ES modules without a build step, and the few third-party scripts are
vendored with their version and licence in `src/Signage.Web/wwwroot/lib/`. Browsers for tests run only inside the
Playwright container.

## Build, run, test

```bash
dotnet build                     # must stay warning-free (warnings are errors)
dotnet test                      # unit + integration tests (Testcontainers starts PostgreSQL in Docker)
scripts/test-e2e.sh              # Playwright browser tests, all inside Docker, against a fresh app + database
docker compose up -d --build     # the app at http://localhost:8080 (with demo content)
docker compose down -v           # stop and wipe the database
```

Run the app from source against the compose database (published on `127.0.0.1:55432`; `appsettings.Development.json`
already points there) at http://localhost:5080:

```bash
docker compose up -d db
dotnet run --project src/Signage.Web
```

Add a migration with the local EF tool (`dotnet tool restore` once):

```bash
dotnet ef migrations add <Name> -p src/Signage.Web -o Data/Migrations
```

GitHub Actions runs the same three on every push and pull request (`.github/workflows/ci.yml`), and publishes the
image to `ghcr.io` for each `v*` tag (`image.yml`).

## Pull requests

`main` is protected: every change arrives through a pull request, and both CI jobs (`build-and-test`, `e2e`) must
pass. Work on a branch per topic (backlog packages use `wp-xx-short-name`), push it, open a pull request into `main`,
and merge with **Create a merge commit**, so each package stays one unit in the history. Stacked branches are fine:
merge them in order.

Useful extras:

- `scripts/test-e2e.sh -trait "Category=Player"` runs a part of the browser tests (xUnit v3 runner options).
- `scripts/update-readme-screenshots.sh` retakes the README screenshots in `docs/images/`.
- `SIGNAGE_E2E_SPIKE=1 scripts/test-e2e.sh -class "Signage.E2E.Spikes.VideoLifecycleSpike"` repeats the video memory
  measurements behind ADR-0008.
- The player accepts `?test=1&timeScale=0.1` to run its timings ten times faster (tests use it). `timeScale` is ignored
  without `test=1`.

## How the code is organised

Read `docs/ARCHITECTURE.md` first, then `docs/PLAYER_SPEC.md` for the browser player. Decisions with real alternatives
are recorded in `docs/adr/`; if you want to change one, add a new ADR rather than editing the old one.

- `src/Signage.Web/Features/<Feature>/`: services and endpoints per feature; `Pages/Admin/`: the Razor Pages admin
  (htmx for list editing); `wwwroot/screen/`: the player; `Domain/` and `Data/`: the model and EF Core.
- `tests/Signage.UnitTests`, `tests/Signage.IntegrationTests` (real PostgreSQL, the app in-process),
  `tests/Signage.E2E` (Playwright).

## Conventions

- **Words:** Screen, Slide, Playlist, Screen Group, Player. Not "monitor", "card" or "feed", in code or in the UI.
- **Every admin string** goes through the resource file (`Resources/SharedResource.resx`); the player's strings live in
  `wwwroot/screen/strings.js`.
- **Time:** inject `TimeProvider`; never `DateTime.Now`/`UtcNow`. Tests use `FakeTimeProvider` or Playwright's clock.
- **Tests with the change:** unit tests for domain logic, integration tests for endpoints, pages and the database,
  browser tests for player behaviour (using the `data-state`/`data-mode` hooks, not sleeps where a wait is possible).
  Don't weaken a test to make it pass; if a test is wrong, say why in the commit.
- **Docs stay true:** if the code changes what ARCHITECTURE or PLAYER_SPEC says, update them in the same change.
- **Commits:** conventional style (`feat:`, `fix:`, `test:`, `docs:`, `chore:`, `refactor:`), one topic each.
- **Public repository hygiene:** no secrets, company names or personal data, and no binaries over 1 MB. Test media is
  generated in a container at test time (`scripts/generate-test-media.sh`).
- **Scope:** `docs/PROJECT_BRIEF.md` lists what is out of scope for now. Ideas for later go to `docs/IDEAS.md`.

## Licence

By contributing you agree that your contributions are licensed under the MIT licence of this project.
