# Review WP-09 — round 2
- Branch: wp-09-video @ 8bcdcde
- Verdict: APPROVE
- Verified:
  - `dotnet build`: Build succeeded, 0 warnings, 0 errors.
  - `dotnet test`: 439 total, 390 passed, 0 failed, 49 skipped (the E2E tests skip outside Docker).
  - `scripts/test-e2e.sh`: Total 49, Errors 0, Failed 0, Skipped 8 (the opt-in spikes). Exit 0.
  - Reviewer probes: two throwaway Playwright tests in a detached worktree in my scratchpad, run with
    `scripts/test-e2e.sh -trait "Category=ReviewProbe"`. Not committed, and the worktree has been removed. The repository is
    untouched.
    - **Probe A (lone carousel video fails, then recovers):** a playlist with only clip-b, timeScale 0.2. The visible
      video's `src` is pointed at a missing file and the clip's host is refused for 6 s, then restored. After 12 s the
      screen has one `active` layer playing clip-b, `loop=true`, `paused=false`. Finding 3's failure path works.
    - **Probe B (single video URL that never answers):** the screen shows one carousel slide. Its mode is then set to
      single video with a URL whose route never responds (no error, no data). After 25 s at timeScale 0.2 there had been
      **1 request**, the **carousel slide was still on screen**, and one never-visible `.single-video` layer existed. See #1.

## Findings
| # | Severity | Location | Finding | Expected |
|---|---|---|---|---|
| 1 | MINOR | `wwwroot/screen/singlevideo.js:68-88`, `docs/PLAYER_SPEC.md` §6 | The single video stall watchdog is only started in the `playing` listener. A URL that never delivers data and never errors (a host that accepts the connection and hangs) never gets `failedAt`. So `show()` never retries it, and `failed` stays false. The carousel slide underneath then stays on a screen set to single video indefinitely (probe B). PLAYER_SPEC §6 now says "no progress for 8 s is tried again" and "stale carousel content never stays", which isn't true in this case. In the carousel, `video.js` arms its watchdog at `play()`, so this gap only exists in single video mode. In practice it's rare: a refused or dropped connection produces an `error` within the browser's network timeout. | Arm the no-progress watchdog when the entry is created (as `video.js` does at `play()`), not on `playing`. Or add a probe-B style test, or defer it as a BACKLOG line. |
| 2 | MINOR | `tests/Signage.E2E/Player/VideoTests.cs` | Two new paths in `carousel.js:164-187` (`keepCurrentPlaying`, failed branch) have no regression test: (a) a *failed* video next to an unloadable slide waits `nothingShowableRetryMs` instead of spinning; (b) a failed lone video is replaced by a fresh element and loops again. I verified both by reading the code, and (b) with probe A, but nothing guards them against regressions. The `ended` variants are tested. | Add a test for (b), e.g. probe A with assertions, and for (a): [video that errors mid-play, 404 image] → request counts stay bounded. Can be deferred as a BACKLOG follow-up. |
| 3 | NIT | `VideoTests.cs:299` | `WaitForTimeoutAsync(5000)` is a fixed sleep. The following assertion (1–2 layers) holds in any case, so this isn't flaky, but it doesn't prove that two retries happened. | Wait for a condition, e.g. a counter of route hits ≥ 2 from the abort handler. |
| 4 | NIT | `carousel.js:173` | `keepCurrentPlaying`'s `idle()` is also ended by any `setManifest` (SSE or poll). A failed video then retries straight away instead of after 10 s. The rate is still bounded by the manifest rate, and a mode change that arrives during the retry waits up to `preloadGraceMs` before it applies. | Optional: check `this.pending` or the mode key after the idle and return early. |

## Requirement check
| R-ID / criterion | Met? | Evidence (test name or file:line) |
|---|---|---|
| E2E: the video doesn't play until fully in view (`currentTime` stays 0 during the transition) | Yes | `VideoTests.A_video_plays_only_once_fully_in_view_and_the_next_slide_comes_when_it_ends`; `carousel.js:354` |
| E2E: the next slide starts on `ended` | Yes | Same test (shown time between the clip length and clip length + 2 s) |
| E2E: error/stall skip | Yes | `A_video_that_cannot_load_is_skipped`, `A_video_that_stops_making_progress_is_skipped_after_8_seconds` (skip between 8 and 11 s after freezing) |
| E2E: single video mode loops with no progress bar | Yes | `Single_video_mode_loops_without_a_progress_bar_and_mode_changes_apply_at_once` |
| E2E: the mode switch applies live | Yes | Same test, plus `Single_video_mode_recovers_from_failures_and_leaves_nothing_behind` (no layer left after a failure) |
| R-22 video lifecycle (PLAYER_SPEC §4.1–4.9, ADR-0008) | Yes | Release before prepare `carousel.js:351`. Replay `video.js` `replay()`. Lone loop: `A_video_that_becomes_the_only_slide_while_it_plays_loops`. Never a tight loop: `A_video_next_to_a_slide_that_cannot_load_plays_again_without_hammering_the_broken_host` |
| R-07 player side (muted unless `playAudio`, muted fallback) | Yes | `A_video_with_sound_plays_with_sound_when_the_browser_allows_it_and_muted_when_it_does_not`, `A_video_with_sound_plays_muted_in_a_browser_that_refuses_autoplay_with_sound`; ADR-0009 records the test approach |
| R-14 single video mode | Yes (see #1) | Loop, no bar, live switch, URL change, failure and retry, release of every layer: `singlevideo.js` entry set, plus the recovery test |
| R-21 progress follows video playback | Yes | First test (fill between 0.05 and 0.95 mid-video); `carousel.js:363-377` |
| Glossary / scope | Yes | No violations; nothing out of scope |
| Docs truthful | Yes, except #1 | PLAYER_SPEC §2/§4.7–4.9/§6 and ADR-0009 match the code apart from the hang case in #1 |

## Previous round follow-up
| Round 1 # | Status | Evidence |
|---|---|---|
| 1 BLOCKER busy loop after a video | Fixed | `carousel.js:148-156` + `keepCurrentPlaying`: an ended video replays with a fresh `finished`, and a failed one waits `nothingShowableRetryMs`. Regression test asserts 1–4 image requests over two full rounds |
| 2 BLOCKER orphaned single video layer | Fixed | `singlevideo.js` tracks every entry in `entries`; `playing` releases all older entries; `hide()` releases all. Regression test `Single_video_mode_recovers_from_failures_and_leaves_nothing_behind` |
| 3 MAJOR lone video freezes | Fixed | Lone branch `carousel.js:128-129` (`setLoop(true)` + `keepCurrentPlaying`); `setLoop` no longer resets on no-op. Test with Clock API; probe A for the failed path |
| 4 MINOR no stall watchdog in single video mode | Mostly fixed | Watchdog after `playing`; the pre-`playing` hang remains (#1) |
| 5 MINOR stale carousel slide under a failing single video | Mostly fixed | `failed` getter → release (`carousel.js:321`), tested with a 404 URL; not for a hanging URL (#1) |
| 6 MINOR missing tests | Fixed, small gap | Stall, lone loop, R-07 both paths, single video failure/retry/URL change are tested; remaining gap in #2 |
| 7 NIT swallowed `play()` rejection | Fixed | `singlevideo.js:91` → `fail()` |
| 8 NIT `setLoop(false)` re-arms the cap every iteration | Fixed | `video.js` `setLoop` returns early when unchanged and only touches the cap while `playing` |

## Notes for the product owner
- All round-1 blockers and the major are fixed, and each has a regression test. My own probes confirm the busy loop is
  gone and that a lone video recovers after a network outage.
- The one remaining gap (#1) is a single video URL whose server accepts the connection but never sends data. The screen
  then keeps showing the previous carousel slide instead of black and retrying. It is rare and easy to fix; it may be
  done now or tracked as a follow-up.
