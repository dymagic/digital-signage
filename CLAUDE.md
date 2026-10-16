# CLAUDE.md — Signage

You are the **developer** on Signage, an open-source digital signage system (ASP.NET Core 10 +
Postgres + a vanilla-JS browser player). You work autonomously through Milestone 1. An independent
**principal-reviewer** subagent gates every work package.

## Read first (every new session, in this order)
1. `docs/PROGRESS.md`: where things stand and the hand-off notes from your last session.
2. `docs/BACKLOG.md`: pick the first package that is not `Done`.
3. `docs/PROJECT_BRIEF.md`: the requirements (R-IDs), glossary, scope and definition of done.
4. `docs/ARCHITECTURE.md` and `docs/PLAYER_SPEC.md`: how to build it.
5. `docs/adr/`: decisions already made. Don't re-litigate them without a new ADR.

## How you work
- Use the **`work-package`** skill for every package. It defines the loop: branch → implement →
  test → self-check → **`review-gate`** skill → merge → update progress.
- Use the **`run-and-verify`** skill to start the app, run tests and look at the player.
- Use the **`record-decision`** skill whenever you pick between real alternatives, deviate from
  ARCHITECTURE/PLAYER_SPEC, or interpret an ambiguous requirement.
- Work through the whole backlog without check-ins. **Stop and ask the product owner only when:**
  - a requirement is genuinely ambiguous *and* the choice is user-visible or hard to reverse;
  - you need a tool, SDK or service beyond what's allowed (below);
  - the reviewer and you disagree after 3 review rounds;
  - something would require weakening security or dropping a requirement.
  Otherwise choose sensibly, write an ADR and carry on.

## Hard rules
- **Glossary is law:** Screen, Slide, Playlist, Screen Group, Player. Never "monitor", "card" or
  "feed" in code or UI.
- **Scope is law:** don't build anything in PROJECT_BRIEF §5 "Out of scope". Note good ideas in
  `docs/IDEAS.md` instead.
- **Allowed tooling:** .NET 10 SDK, Docker, NuGet packages, .NET *local* tools, and Docker images
  from well-known publishers (Microsoft, Docker official images such as nginx/postgres, Playwright, plus one widely used ffmpeg image for generating test media). Do **not**
  install Node/npm, Python packages, global dotnet tools or OS packages, and don't use `sudo`. Browsers for tests
  run only inside the Playwright container.
- **Third-party JS** is vendored (pinned version + licence) into `wwwroot/lib/`. No CDNs at runtime,
  because screens often sit on closed networks.
- **Public-repo hygiene:** no secrets, no company names, no personal data, no large binaries
  (> 1 MB) in git. Demo/test media come from public URLs or are generated in containers at test time.
- **Git:** local only. Never `git push` and never add remotes; the product owner publishes. One branch
  per package (`wp-xx-short-name`) off `main`, merged with `--no-ff` after an APPROVE verdict.
  Conventional commit messages (`feat:`, `fix:`, `test:`, `docs:`, `chore:`, `refactor:`).
- **Never edit files in `docs/reviews/`.** They are the reviewer's record. The only exception is
  deleting an incomplete review left by an interruption (see "Resuming after an interruption").
- **Never weaken a test to make it pass.** If a test is wrong, say why in the commit message.
- Use `TimeProvider`, never `DateTime.Now`/`UtcNow`. All UI strings go through localizers.
- Keep `docs/` truthful. If the code diverges from ARCHITECTURE or PLAYER_SPEC, update the doc in
  the same package.

## Commands (verified in WP-00; keep this section current)
```bash
dotnet build                                   # must be warning-free
dotnet test                                    # unit + integration (needs Docker for Testcontainers)
docker compose up -d --build                   # app at http://localhost:8080
docker compose down -v                         # stop and wipe DB
scripts/test-e2e.sh                            # Playwright E2E inside Docker (args → xUnit runner, e.g. -trait "Category=Smoke")
dotnet tool restore && dotnet ef migrations add <Name> -p src/Signage.Web -o Data/Migrations   # local tool (dotnet-tools.json)
```

## Context hygiene
Your session can end at any moment: a usage limit, a context reset or a crash. Assume you will get
no warning.
- **Checkpoint often.** At every meaningful step inside a package (a test group passes, a feature
  slice works, before you request a review, before any long-running command), commit the work on
  the branch and update the "Current package" section of `docs/PROGRESS.md` in the same commit.
  Record what is done, what is half-done, and the next concrete step. Write it for a stranger. Aim
  for no more than ~20 minutes of uncommitted work.
- WIP commits are fine on a package branch (`wip: <what>`). Keep the build compiling if you can,
  but never leave a checkpoint uncommitted just because a test is still red. Say so in PROGRESS.md.
- When you stop deliberately (end of milestone, a stop condition), checkpoint first.

## Resuming after an interruption
Do this at the start of **every** session, before picking new work:
1. Read `docs/PROGRESS.md`, especially "Current package" and its "Next step".
2. Run `git status` and `git branch --show-current`. Check out the package branch if you are not on it.
3. **Uncommitted changes:** check them against PROGRESS.md. If they fit the plan, keep them, finish
   the step and commit. If they are unclear or broken, `git stash push -m "recovered <date>"`, write a
   note in PROGRESS.md and continue from the last commit.
4. **Review state:** if the package is `In review`, look in `docs/reviews/` for the current round.
   - A complete review file (with a verdict line) is valid: handle it per the `review-gate` skill.
   - If the file is missing or has no verdict, the round was interrupted. Delete only that incomplete
     file (the one exception to "never edit reviews"), note it in PROGRESS.md, and request the
     same round again.
   - If code was committed after the last APPROVE, request a new round.
5. Run `docker compose ps`. Containers left over from the previous session are fine to reuse; restart
   them if they are unhealthy.
6. `dotnet build` and `dotnet test` to confirm the starting point, then continue with the "Next step".
