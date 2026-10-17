# Review WP-07 — round 1
- Branch: wp-07-video-spike @ fa900b7
- Verdict: CHANGES_REQUIRED
- Verified:
  - `dotnet build`: succeeded, 0 warnings, 0 errors.
  - `dotnet test`: Passed, total 390, succeeded 370, failed 0 (the 20 E2E tests skip outside Docker).
  - `scripts/test-e2e.sh`: exit 0. Signage.E2E total 20, failed 0, skipped 7 (all Category=Spike, which is gated as intended).
    The new `testmedia` service and `generate-test-media.sh` run cleanly.
  - `SIGNAGE_E2E_SPIKE=1 scripts/test-e2e.sh -trait "Category=Spike"`: exit 0, 7 tests, 2 skipped (the long run), 897 s.
    **The ADR's 240-transition table reproduces.** PSS at 240 in my run: release 569, remove 838, reuse 613, keep 6703 MB
    (the ADR has 569 / 839 / 612 / 6698). Destroyed media players: 120 / 119 / 119 / 0. `errors` was empty and `maxLayers` was 2
    (keep: 122). Codec probe: H.264+AAC is "probably" and plays (`currentTime` 0.47 s after 500 ms).
  - I didn't rerun the 1200-transition long run (≈30 min). I read the developer's raw CSVs in `artifacts/e2e/spike/`, and they
    match the ADR tables.

## Findings
| # | Severity | Location | Finding | Expected |
|---|---|---|---|---|
| 1 | MAJOR | `docs/adr/0008-video-lifecycle.md:149-159` | The spike exists to make sure screens survive weeks of running. The ADR's own data doesn't support its conclusion about the chosen strategy. After 1200 transitions, *release* still grows (+34 MB over 800→1200, +29 MB over 1000→1200), while the images-only baseline is flat at 317–319 MB from transition 600 on. Calling this "small and slowing … not a per-video leak" is an interpretation without evidence. Element, node and player counts rule out a *DOM/JS* leak, but they don't explain process memory. The two release runs also disagree on rate. The 240 run (sampled every 20) goes 333→569 MB between t=20 and t=240 (≈1 MB/transition). The 1200 run (sampled every 100) is at 429 MB at t=200. The ADR's "about 0.3 MB per transition early on" fits neither. So the instrument may be part of what's measured: each sample does a forced GC, CDP calls and Playwright evaluates, **with Playwright tracing on (screenshots + DOM snapshots)**, because `E2ETest.InitializeAsync` starts tracing for every test (`tests/Signage.E2E/Infrastructure/E2ETest.cs:36`). The planned follow-up can't detect the problem either. The WP-13 30-minute soak at real slide durations covers roughly 100–200 transitions. At ≈0.1 MB per transition (≈0.2 MB per video play), a playlist with a video a minute adds ≈250–300 MB a day, which matters on a Pi. | Turn tracing off for the spike runs and use the same sampling interval for both arms. Rerun release against images-only long enough to show a plateau (e.g. ≥3000 transitions), or show that there isn't one. Then either (a) show the plateau in the ADR, or (b) record the growth honestly as an open risk with a projected MB/day for a realistic mix, and add a concrete follow-up line to `docs/BACKLOG.md` (WP-09 or WP-13). That follow-up should be an accelerated video soak of the *real* player (via `timeScale`) with ≥1000 video plays and a pass threshold, not just the 30-minute soak. Fix the per-transition rates in the text so they match the tables. |
| 2 | MAJOR | `tests/Signage.E2E/spikes/video-lifecycle/harness.js:506-513`; ADR §Decision; `docs/PLAYER_SPEC.md` §4 | PLAYER_SPEC §4 explicitly asks "how preloading the next video interacts with releasing the current one". The harness sequence strictly alternates video/image, so a video is never prepared while another video plays, and a covered video is never released while a new video preloads. The video→video case (a playlist of several videos in a row, which is common) was never run. On a Pi it is exactly the case that needs two decoder instances at once. The ADR still states "no more than two video elements exist at any time", but that wasn't measured. It also depends on an ordering the spec doesn't state: §2 says the next slide is prepared "when a slide becomes active", which is the same moment the covered slide is cleaned up. If the next slide is prepared before the covered video is released, a V→V→V sequence briefly has three. | Add a video-only (or V,V,I) sequence option to the harness and run *release* with it. Report PSS, players created/destroyed, errors, and whether the next video reaches `canplay` before its turn. In the ADR, answer the preloading question with that data. In PLAYER_SPEC §4, state the order: release the covered video first, then start preparing the next slide. |
| 3 | MINOR | `tests/Signage.E2E/Spikes/VideoLifecycleSpike.cs:350` | The `playersCreated` counter listens for `Media.playersCreated`, which never fires in this Chromium. The `playersCreated` column is 0 in every CSV, both the developer's and mine. The ADR's "/ 120" numbers come from the `kWebMediaPlayerCreated` entries in `mediaEvents`, so the CSV contradicts the table. | Count creations from `kWebMediaPlayerCreated` in `playerEventsAdded` (as destroyed is counted), or drop the column. Say in the ADR where each number comes from. |
| 4 | NIT | ADR §Data, table header | The table header says "Players destroyed / created", but these are Chromium media pipelines (`WebMediaPlayer`). In this project "Player" is a glossary term for the browser app. | Call them "media players (Chromium WebMediaPlayer)" or "media pipelines". |
| 5 | NIT | ADR §Data | Each sample forces a GC, and *remove* only frees its players at GC. A real player never forces a GC, so *remove* would do worse in the field than the table shows. That supports the decision and is worth saying. The codec probe result (H.264/AAC plays in the Playwright Chromium) is also useful for WP-09 and isn't recorded. | One sentence each in the ADR. |

## Requirement check
| R-ID / criterion | Met? | Evidence (test name or file:line) |
|---|---|---|
| Throwaway harness in `tests/Signage.E2E/spikes/`, kept but not shipped | Yes | `tests/Signage.E2E/spikes/video-lifecycle/harness.{html,js}`. It's served only by the e2e-profile nginx (`docker-compose.e2e.yml` `testmedia`), not by the app. |
| Cycles videos and images with the planned transition | Yes (shortened) | `harness.html:477-479`. translateX, the PLAYER_SPEC easing and shadow, 300 ms instead of 900 ms (stated in the ADR). |
| 200+ transitions under different strategies | Yes | `VideoLifecycleSpike.Measure` × 4 strategies × 240. `MeasureLong` 1200. Reproduced by me. |
| Openly licensed media from a local container, no large binaries committed | Yes | `scripts/generate-test-media.sh` (ffmpeg lavfi test patterns into git-ignored `artifacts/media`). Nothing binary in the diff. |
| PLAYER_SPEC §4: release hypothesis | Yes | ADR table: release destroys 120/120 at cover time and has the lowest PSS. |
| PLAYER_SPEC §4: reuse vs. fresh elements | Yes | ADR "Why not reuse". It reproduces: reuse ends 44 MB above release, and the late-event argument is sound. |
| PLAYER_SPEC §4: memory/handle trend over 200+ transitions | Partly | Trend measured, but the residual growth of the chosen strategy isn't explained or bounded (finding 1). |
| PLAYER_SPEC §4: preloading the next video vs. releasing the current one | No | Not exercised by the harness (finding 2). |
| Raspberry Pi implications from the literature | Yes | ADR §Consequences (Pi 4 V4L2 H.264, Pi 5 without an H.264 HW decoder). Accurate as far as I know. |
| Output ADR with data, recommendation, decision; PLAYER_SPEC updated | Yes, pending 1–2 | `docs/adr/0008-video-lifecycle.md`, `docs/PLAYER_SPEC.md:55-57` |
| R-22 (resource handling decided by the spike, recorded in an ADR) | Yes, pending 1–2 | ADR-0008 |

## Notes for the product owner
- The headline result is solid and reproducible: explicitly releasing covered videos (`pause` → drop `src` → `load` → remove) is
  clearly right. Doing nothing grows by ≈27 MB per transition (6.7 GB after 240).
- The open question is whether a screen still creeps up slowly over days even with release. The current data suggests roughly 0.1 MB
  per transition with no clear plateau yet. That needs either a proper bound or an explicit follow-up that tests the real player
  long enough to see it. A periodic self-reload of the player would be a cheap mitigation if it turns out to be real, but that's
  your call and outside this package.
