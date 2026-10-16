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

## If usage runs out mid-development
Claude Code stops and shows when your usage resets. Nothing is lost beyond (at most) the last few
minutes of uncommitted edits, because the developer checkpoints to its branch and PROGRESS.md as it
goes. Once usage resets:
- **Same session still open:** type `continue`.
- **New session or after a context reset:** paste the prompt above again. The session runs the
  "Resuming after an interruption" checklist in CLAUDE.md first. That checklist covers uncommitted
  changes, an interrupted review, and leftover containers.

`main` only ever contains reviewed, merged packages, so an interruption can't damage it.

## Optional: your own independent review
At any point you can open a **separate** session and ask:
```
Act as the principal-reviewer agent defined in .claude/agents/principal-reviewer.md.
Review MILESTONE (whole repository) as round 1 and write the review file.
```
