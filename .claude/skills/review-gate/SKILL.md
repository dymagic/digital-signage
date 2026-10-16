---
name: review-gate
description: Request an independent review of a Signage work package (or the milestone) from the principal-reviewer subagent and handle its verdict. Required before any work package is merged.
---

# Review gate

## Before requesting
Commit everything and note "Requested review round n" in PROGRESS.md. If the session dies during the
review, the next session can then see what was going on. An incomplete review file found later is
handled per "Resuming after an interruption" in CLAUDE.md.

## Request
Invoke the `principal-reviewer` subagent (Agent tool, `subagent_type: principal-reviewer`). Run it in the
foreground, because your next step depends on it. Keep the prompt **factual and minimal**. Don't argue your
case or describe how good the work is. The reviewer must form its own view.

Prompt template:
```
Review <WP-xx | MILESTONE>.
Branch: <branch>   Base: main   Round: <n>
Package definition: docs/BACKLOG.md → <WP-xx>.
<Round ≥ 2 only: "Previous review: docs/reviews/<WP-xx>-review-<n-1>.md. Fix commits: <sha list>.">
```
Don't pass your plan, PROGRESS.md content or explanations of design choices. If a choice needs
explaining, it belongs in an ADR, which the reviewer reads.

## Handle the verdict
1. Read the review file in `docs/reviews/`. Commit it on the branch unchanged
   (`docs: review WP-xx round n`).
2. `APPROVE` → go back to the work-package skill, step 6. Deferred MINOR items go into BACKLOG.md.
3. `CHANGES_REQUIRED` →
   - Fix every BLOCKER and MAJOR. Fix MINORs unless clearly better deferred.
   - If you believe a finding is wrong, don't ignore it. Write a short rebuttal with evidence in an
     ADR or in `docs/reviews/<WP-xx>-response-<n>.md`, and reference it in the next round.
   - Re-run the full self-check, then request round n+1.
4. **Escalation:** if round 3 still returns `CHANGES_REQUIRED`, or the reviewer raises something that
   needs a product decision, stop and ask the product owner. Summarise the disagreement neutrally
   and link the review files.

## Rules
- Never edit or delete a reviewer-written file.
- Never merge without an `APPROVE` file for the current branch head (or an explicit product-owner override
  recorded in PROGRESS.md).
- If a later commit changes code after the APPROVE, that needs a new round.
