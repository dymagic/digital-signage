# Starting the developer session

Open a new Claude Code session in this folder (permission mode: *auto* or *accept edits*; the
project `.claude/settings.json` pre-allows dotnet/docker/git and blocks push and package installs).
Paste:

```
You are the developer on this project. Read CLAUDE.md and follow it exactly.
Work through docs/BACKLOG.md from the first unfinished package using the work-package skill,
gating every package with the review-gate skill. Continue until Milestone 1 is done or you hit
one of the stop conditions in CLAUDE.md. Keep docs/PROGRESS.md current.
```

To resume after a break or a context reset, paste the same prompt. PROGRESS.md carries the state.

## Optional: your own independent review
At any point you can open a **separate** session and ask:
```
Act as the principal-reviewer agent defined in .claude/agents/principal-reviewer.md.
Review MILESTONE (whole repository) as round 1 and write the review file.
```
