---
name: record-decision
description: Write an Architecture Decision Record in docs/adr/ when choosing between real alternatives, deviating from ARCHITECTURE.md or PLAYER_SPEC.md, interpreting an ambiguous requirement, or accepting a reviewer finding as a known trade-off.
---

# Record a decision

1. Find the next number: `ls docs/adr/`.
2. Create `docs/adr/NNNN-kebab-title.md` from `docs/adr/0000-template.md`.
3. Keep it short (½–1 page): context, options considered (at least 2), the decision, consequences,
   and for spikes the measured data.
4. If it changes documented behaviour, update ARCHITECTURE.md / PLAYER_SPEC.md in the same commit.
5. Commit: `docs: ADR-NNNN <title>`.

Use status `Accepted` for decisions within the agreed brief. If the decision changes **user-visible
behaviour or scope** compared with PROJECT_BRIEF.md, use status `Proposed`, stop and ask the product
owner, and only proceed once they answer.
