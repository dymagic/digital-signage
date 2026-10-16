---
name: run-and-verify
description: Start the Signage app, run unit/integration/E2E tests, and visually verify the admin and the screen player (screenshots via Playwright in Docker or the built-in browser). Use whenever you need to see a change working, debug the player, or check timing/animation behaviour.
---

# Run and verify

## Start the stack
```bash
docker compose up -d --build
docker compose ps                  # app and db healthy?
curl -fsS http://localhost:8080/readyz
docker compose logs app --tail=100
```
First run: open `http://localhost:8080/admin`, which shows the setup page, or set the bootstrap env vars in
a local `docker-compose.override.yml`. That file is git-ignored: never commit credentials. Demo
content: `SIGNAGE__SEEDDEMO=true`.

For a fast inner loop you may run the app on the host instead (`dotnet watch --project src/Signage.Web`)
against the compose Postgres (`docker compose up -d db`).

## Tests
```bash
dotnet test                                     # unit + integration (Testcontainers needs Docker)
dotnet test --filter "FullyQualifiedName~Resolver"
scripts/test-e2e.sh                             # all E2E in the Playwright container
scripts/test-e2e.sh --filter "Category=Player"  # subset (keep the script supporting pass-through args)
```
E2E artefacts (traces, screenshots, videos on failure) go to `artifacts/e2e/`, which is git-ignored. Open
the PNGs with the Read tool to inspect them.

## Looking at the player
- Use the built-in browser if it's available (`http://localhost:8080/screen?preview=<screenId>` while logged in
  as admin), or take screenshots via an E2E test.
- For transitions, capture a sequence: with `?test=1&timeScale=1`, take screenshots at 0, 150, 300,
  600 and 900 ms into a transition. Check that the outgoing slide doesn't move, the incoming slide has the
  left-edge shadow, and the easing feels firm (fast start, soft settle).
- For timing assertions, prefer `data-state` attributes and `page.WaitForFunctionAsync` over sleeps.
- For memory checks (video spike, soak), use CDP via Playwright (`Performance.getMetrics`, JS heap)
  and `docker stats` on the browser container. Record the numbers in the ADR or acceptance doc.

## Test media
Don't commit video files. Generate small MP4s at test time inside a container, e.g.
`docker run --rm -v "$PWD/artifacts/media:/out" linuxserver/ffmpeg -f lavfi -i testsrc=duration=5:size=1280x720:rate=30 -pix_fmt yuv420p /out/test5s.mp4`,
and serve them from an nginx service in `docker-compose.e2e.yml`.

## Clean up
`docker compose down` (add `-v` to wipe the DB). Leave nothing running at the end of a session.
