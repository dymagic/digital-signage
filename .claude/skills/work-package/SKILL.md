---
name: work-package
description: The development loop for one Signage backlog work package (WP-xx) — pick, branch, implement, test, self-check, review gate, merge, record progress. Use whenever starting or resuming work on the backlog.
---

# Work package loop

## 1. Pick
- Read `docs/PROGRESS.md` and `docs/BACKLOG.md`. Resume any package that is `In progress`/`In review`.
  Otherwise take the first `Todo`.
- Re-read its R-IDs in `docs/PROJECT_BRIEF.md` and the relevant sections of ARCHITECTURE / PLAYER_SPEC.
- Write a short plan (files, tests, risks) in `docs/PROGRESS.md` under "Current package".

## 2. Branch
```bash
git switch main && git switch -c wp-xx-short-name
```
Set the package to `In progress` in BACKLOG.md and commit (`chore: start WP-xx`).

## 3. Implement test-first where practical
- Domain logic (resolver, schedule, pairing codes): write unit tests first.
- Endpoints, pages, authz, DB: integration tests with `WebApplicationFactory` + Testcontainers.
- Player behaviour: Playwright E2E using the `data-state` hooks and `?test=1&timeScale=`.
- Commit in small, coherent steps with conventional messages.
- If you face a real decision, use the `record-decision` skill before moving on.

## 4. Self-check before review (don't skip any line)
- [ ] `dotnet build` with zero warnings
- [ ] `dotnet test` green
- [ ] `scripts/test-e2e.sh` green (always from WP-06 onward)
- [ ] Every acceptance criterion and R-ID of the package is covered by a named test or a written manual check
- [ ] Looked at the result (run-and-verify skill): screenshots for UI/player changes
- [ ] Glossary respected; nothing out of scope added; UI strings localised
- [ ] Docs updated (ARCHITECTURE/PLAYER_SPEC/README/CLAUDE.md commands) where reality changed
- [ ] `git status` clean, no secrets or large files

## 5. Review gate
Set the status to `In review`, commit, then follow the `review-gate` skill. Loop until `APPROVE`.

## 6. Merge & record
```bash
git switch main && git merge --no-ff wp-xx-short-name -m "Merge WP-xx: <title>"
git branch -d wp-xx-short-name
```
- BACKLOG.md: status `Done`. Add any deferred MINOR findings as follow-up lines under the next
  suitable package.
- PROGRESS.md: move the package to "Completed" with one line of outcome. Clear "Current package". Write the
  next step.
- Commit (`docs: complete WP-xx`) on main.

## 7. Continue
Start the next package straight away. When WP-13 is done, run the review gate with `MILESTONE`, then
stop and report to the product owner: a summary, how to run it, and anything they should decide.
