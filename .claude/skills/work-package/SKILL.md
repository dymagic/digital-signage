---
name: work-package
description: The development loop for one Signage backlog work package (WP-xx) — pick, branch, implement, test, self-check, review gate, hand over for a pull request, record progress. Use whenever starting or resuming work on the backlog.
---

# Work package loop

## 0. Resume check
Always start with the "Resuming after an interruption" steps in CLAUDE.md. They tell you whether you are
picking up a half-done package.

## 1. Pick
- Read `docs/PROGRESS.md` and `docs/BACKLOG.md`. Resume any package that is `In progress`/`In review`.
  Otherwise take the first `Todo`.
- Re-read its R-IDs in `docs/PROJECT_BRIEF.md` and the relevant sections of ARCHITECTURE / PLAYER_SPEC.
- Write a short plan (files, tests, risks) in `docs/PROGRESS.md` under "Current package".

## 2. Branch
Packages reach `main` through pull requests on GitHub, which the product owner merges. Don't wait for that:
```bash
git switch main
git merge-base --is-ancestor wp-yy-previous main && echo merged   # is the previous package in main yet?
git switch -c wp-xx-short-name main               # yes: start from main
git switch -c wp-xx-short-name wp-yy-previous     # no: stack on the previous package's branch
```
A stacked branch is fine: its pull request shows only its own commits once the one below is merged. Note the
stacking in PROGRESS.md. Set the package to `In progress` in BACKLOG.md and commit (`chore: start WP-xx`).

## 3. Implement test-first where practical
- Domain logic (resolver, schedule, pairing codes): write unit tests first.
- Endpoints, pages, authz, DB: integration tests with `WebApplicationFactory` + Testcontainers.
- Player behaviour: Playwright E2E using the `data-state` hooks and `?test=1&timeScale=`.
- Commit in small, coherent steps with conventional messages.
- **Checkpoint:** at each meaningful step, update "Current package" in PROGRESS.md (done /
  half-done / next step) in the same commit as the code. Do this before requesting a review and
  before long-running commands too. Never sit on more than ~20 minutes of uncommitted work.
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

## 6. Record and hand over for a pull request
Don't merge into `main` locally and never push: the product owner pushes the branch and merges the pull request.
- On the package branch, BACKLOG.md: status `Done`. Add any deferred MINOR findings as follow-up lines under the
  next suitable package.
- PROGRESS.md: move the package to "Completed" with one line of outcome, ending in
  `— branch wp-xx-short-name (pull request)`. Clear "Current package". Write the next step.
- Commit (`docs: complete WP-xx`) on the branch. The `APPROVE` review must still be for the code at this head:
  this commit changes only BACKLOG.md and PROGRESS.md.
- Tell the product owner in one line that the branch is ready, with the commands:
  ```bash
  git push -u origin wp-xx-short-name
  ```
  then open a pull request into `main` on GitHub; CI must pass; merge with **Create a merge commit** (the history
  keeps each package as one merge). If the branch is stacked, say which pull request must be merged first.
- Local branches already merged into `main` can be deleted: `git branch -d <branch>`.

## 7. Continue
Start the next package straight away (step 2), without waiting for the pull request. When the last package of a
milestone is done, run the review gate with `MILESTONE` on a branch that contains all the packages (the last package
branch, or `main` once everything is merged), then stop and report to the product owner: a summary, how to run it,
which pull requests are waiting, and anything they should decide.
