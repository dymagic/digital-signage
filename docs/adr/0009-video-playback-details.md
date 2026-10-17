# ADR-0009: Video playback details

- Status: Accepted
- Date: 2026-10-18
- Work package: WP-09
- Requirements: R-07, R-14, R-21, R-22

## Context
PLAYER_SPEC §4 and §6 left four points open or worded narrowly: when the 10-minute safety cap applies, what happens
when a video with sound is refused autoplay, whether a mode change waits for the next transition, and whether video
timings follow the test `timeScale`.

## Options considered
1. **Cap:** only for videos without duration metadata, *or* for every video slide.
2. **Sound refused:** skip the slide (it can't play as configured), *or* play it muted and log a warning.
3. **Mode change:** at the next transition (like other manifest changes), *or* at once.
4. **Test scale:** scale the stall limit and the cap, *or* keep them real time.

## Decision
1. **Every video slide** has the 10-minute cap. A file can report a duration and still never fire `ended` (a broken
   stream, a live source); one rule is simpler and safe. Longer videos belong in single video mode.
2. **Play muted.** R-07 says sound needs a kiosk started with an autoplay policy. A screen without it should still show
   the video; skipping it silently would look like a bug. The warning goes to the console now and to the heartbeat from
   WP-10.
3. **At once.** PLAYER_SPEC §9 already says "mode changes apply immediately"; R-14 says "applies live". The current
   slide's dwell is cut short, and the change cross-fades (300 ms).
4. **Real time.** Videos play at normal speed in tests, so a scaled stall limit would fire falsely.

## Consequences
- PLAYER_SPEC §4 and §6 say this.
- A screen playing a video longer than 10 minutes as a slide moves on at 10 minutes. The admin hint can mention it in
  WP-12 if needed.
- Heartbeat reporting of skipped videos is part of WP-10 (BACKLOG).
