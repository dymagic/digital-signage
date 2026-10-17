# Review WP-09 — round 1
- Branch: wp-09-video @ fff238d
- Verdict: CHANGES_REQUIRED
- Verified:
  - `dotnet build`: Build succeeded, 0 warnings, 0 errors.
  - `dotnet test`: 433 total, 390 passed, 0 failed, 43 skipped (the E2E tests skip outside Docker).
  - `scripts/test-e2e.sh`: Total 43, Failed 0, Skipped 8 (the opt-in spikes). Exit 0.
  - Reviewer probes: two throwaway Playwright tests run in a detached worktree in my scratchpad, not committed. The
    repository is untouched.
    - **Probe 1:** a playlist of [video clip-b (3 s), image returning 404], timeScale 1. In about 8 s after the first
      activation the player sent **1609 requests** for the image. The video sat at `ended=true paused=true t=3`.
    - **Probe 2:** single video mode with clip-c. The visible video gets an `error` event and the media route is
      aborted for 4 s, then restored. Then the screen goes back to carousel mode. Result: 2 `.single-video` layers
      existed while in single video mode. After the switch, `body[data-mode=player]` was set but one layer was left as
      `single-video visible`, opacity 1. The screenshot shows the old video covering the whole carousel.

## Findings
| # | Severity | Location | Finding | Expected |
|---|---|---|---|---|
| 1 | BLOCKER | `wwwroot/screen/carousel.js:135-151`, `slides/video.js:19-35` | A video slide whose `finished` has already resolved (on `ended`, `error`, a stall or the cap) stays current when `advance()` can't show anything else, e.g. the other slide is a broken image or its host is down. On the next loop iteration `dwell()` returns at once (`await current.item.finished` is already settled), the next slide is prepared again, fails again, and so on, with no delay. Measured (probe 1): about 200 requests per second against the image host, the CPU spinning, and the video frozen on its last frame. The `A_video_that_cannot_load_is_skipped` test misses this because its neighbours are Html slides. On a real office screen this is a busy loop that hammers a third-party host. | Make "the current slide stays for another round" (PLAYER_SPEC §2) mean a real wait for an ended or failed video: replay it from 0 with a fresh `finished`, watchdog and cap, or back off (`nothingShowableRetryMs`). Add an E2E regression test: [video, 404 image] counts requests over a few seconds and asserts the number stays bounded. |
| 2 | BLOCKER | `wwwroot/screen/singlevideo.js:36-39, 60-65` | A visible single video that fails mid-play can be left behind as an orphaned layer. Retry 1 creates entry 2, and entry 2's `playing` listener holds entry 1 as `previous`. If entry 2 also fails (very likely when the network is out for more than 10 s), retry 2 releases entry 2 as "never visible" and sets `previous = null`. Entry 1 is then never released. It stays in the DOM at `z-index: 10`, opacity 1, black background, above the carousel. `hide()` only knows about `this.current`, so after a switch back to carousel the screen keeps showing the old single video, or black, until the daily reload (probe 2). | Track every superseded layer. When releasing a non-visible `previous`, pass its own pending `previous` on to the new entry, or keep a list of layers to release. `hide()` and `stop()` must remove every `.single-video` layer. Add a regression E2E test: visible single video errors → a retry fails → recovery → switch to carousel → no `.single-video` left. |
| 3 | MAJOR | `wwwroot/screen/carousel.js:114-128`, `slides/video.js:76-82` | "A single video loops" fails when the video *becomes* the only playable slide while it plays. Two ways to get there: a neighbour's schedule window ends, or a neighbour is removed from the playlist. `dwell` waits for `ended`, `advance()` finds nothing else, and the one-slide branch calls `setLoop(true)` on a video that has already ended. Setting `loop` doesn't restart playback, so the screen shows a frozen last frame indefinitely. The same applies to a lone video that failed or stalled: its watchdog has stopped and nothing ever retries it. | In `setLoop(true)`, if the video has ended, restart it (`currentTime = 0; play()`). Retry a failed or stalled lone video after `nothingShowableRetryMs` (fresh element per ADR-0008). Add a test: [Html with `activeUntil` just after start (Clock API) or a removed neighbour, video] → the video keeps looping (`currentTime` wraps). |
| 4 | MINOR | `wwwroot/screen/singlevideo.js` | Single video mode has no stall watchdog. A video that stalls without an `error` event (network gone mid-stream, server hangs) freezes for good. PLAYER_SPEC §6 promises "a video that fails is tried again after 10 s", and §4.4 treats a stall longer than 8 s as a failure. | Apply the same 8 s no-progress rule (real time) and retry. |
| 5 | MINOR | `wwwroot/screen/carousel.js:274-290` | If the single video URL never plays (404, unsupported codec), the last carousel slide stays on screen indefinitely while `data-mode=single-video`. If that slide is a video with `playAudio`, it keeps playing with sound until it ends and then freezes. The admin sees the mode saved, but the screen shows stale carousel content. | After the first failure, release the carousel slide and show the empty state (or black) until a retry succeeds. Say so in PLAYER_SPEC §6. |
| 6 | MINOR | `tests/Signage.E2E/Player/VideoTests.cs` | Several paths have no test: the stall skip (the package text says "error/stall skip"); a lone video looping in carousel mode; the R-07 player side with `playAudio=true` (unmuted, and the muted fallback on `NotAllowedError`); single video failure and retry; a URL change while in single video mode. Findings 1–3 would have been caught by such tests. | At least: a stall test (route a video that sends headers and part of the body, then hangs), a lone-video loop test, and the regression tests for findings 1–3. R-07: assert `muted=false` on a `playAudio` slide in a context whose autoplay policy allows sound, if the container's Chromium permits it. Otherwise record why it can't be tested in the ADR. |
| 7 | NIT | `wwwroot/screen/singlevideo.js:68` | `play().catch(() => {})` swallows rejections that don't come with an `error` event, e.g. `NotAllowedError`. `failedAt` stays null, so the mode never retries. | Log the rejection and set `failedAt` so the 10 s retry applies. |
| 8 | NIT | `wwwroot/screen/slides/video.js:76-82` | `setLoop(false)` runs on every loop iteration. It restarts the 10-minute cap, and on a finished item it arms a timer that does nothing. That's harmless today, but easy to get wrong when fixing finding 1. | Re-arm the cap only when `loop` actually changes, and not after `done`. |

## Requirement check
| R-ID / criterion | Met? | Evidence (test name or file:line) |
|---|---|---|
| E2E: the video doesn't play until fully in view (`currentTime` stays 0 during the transition) | Yes | `VideoTests.A_video_plays_only_once_fully_in_view_and_the_next_slide_comes_when_it_ends`: every frame of the transition has `currentTime == 0` and `paused` (≥ 10 frames); `carousel.js:318` |
| E2E: the next slide starts on `ended` | Yes | Same test: the time on screen is between the clip length and clip length + 2 s (a 3 s clip against a 10 s default) |
| E2E: a broken URL is skipped | Yes, but see #1 | `VideoTests.A_video_that_cannot_load_is_skipped` |
| E2E: single video mode loops with no progress bar | Yes | `VideoTests.Single_video_mode_loops_without_a_progress_bar_and_mode_changes_apply_at_once` (wrap-around check, progress hidden) |
| E2E: the mode switch applies live | Yes | Same test: one activation only, and the carousel slide released; switching back works. #2 breaks the switch back after a failure |
| R-22 video lifecycle (PLAYER_SPEC §4.1–4.8, ADR-0008 release order) | Partly | §4.1/4.2/4.3: tested. §4.4 stall: implemented (`video.js:54-61`) but untested (#6). §4.7 lone video loops: broken in the case in #3. Release order per ADR-0008: `carousel.js:315` before the next `prepare` |
| R-07 player side (muted unless `playAudio`, muted fallback) | Partly | Muted default tested. `playAudio` and the fallback path (`video.js:63-72`) untested (#6) |
| R-14 single video mode | Partly | Loop, no bar and live switch tested. Failure handling is defective (#2, #4, #5) |
| R-21 progress follows video playback | Yes | Fill between 0.05 and 0.95 mid-video in the first test; `carousel.js:327-341` |
| Glossary / scope | Yes | No violations in the diff. Nothing out of scope |
| Docs truthful (PLAYER_SPEC §2/§4/§6, ARCHITECTURE module list, ADR-0009) | Yes | ADR-0009's choices (cap on every video, muted fallback, immediate mode change, real-time limits) are sensible and I agree with them |
| Carried in from WP-08 round 3: E2E for expired/removed slide → empty state | Yes | `ClockTests.A_slide_whose_schedule_ended_is_not_kept_on_screen_when_nothing_else_can_be_shown` |
| Carried in: the "settles" check waits past a repeat poll | Yes | `CarouselTests.cs:183` (7 s > 6 s poll at this scale) |
| Carried in: `!this.stopped` guard on the empty-state fallbacks | Yes | `carousel.js:361` (both fallbacks go through `showEmptyState`) |
| Carried in: PLAYER_SPEC §2 "Skipping" mentions the expired/removed exception | Yes | PLAYER_SPEC §2 |
| Heartbeat reporting of skipped videos | Deferred | Moved to WP-10 in BACKLOG with a reason (ADR-0009). Accepted |

## Notes for the product owner
- The happy paths work well and are tested carefully: the video stays at frame 0 during the slide-in, the next slide
  starts on `ended`, the progress bar follows playback, and the live mode switch works. The problems are all in
  failure and edge paths: a broken neighbour next to a video, a network outage in single video mode, and a video left
  alone in its playlist. Those paths matter on an office screen left unattended for weeks.
- ADR-0009 decides that every video slide is capped at 10 minutes. Longer videos belong in single video mode. That's
  user-visible; the admin hint is planned for WP-12.
