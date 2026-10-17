# Review WP-07 — round 2
- Branch: wp-07-video-spike @ 32e84ce
- Verdict: CHANGES_REQUIRED
- Verified:
  - `dotnet build`: succeeded, 0 warnings, 0 errors.
  - `dotnet test`: total 391, succeeded 370, failed 0, skipped 21 (the E2E tests skip outside Docker).
  - `scripts/test-e2e.sh`: exit 0. Signage.E2E total 21, failed 0, skipped 8 (all spike tests, gated as intended). The
    tracing toggle in `E2ETest` doesn't affect the normal suite.
  - `SIGNAGE_E2E_SPIKE=1 SIGNAGE_E2E_SPIKE_LONG=1 scripts/test-e2e.sh -method "Signage.E2E.Spikes.VideoLifecycleSpike.MeasureLong"`:
    exit 0, 3 tests, 3971 s. **The ADR's 2000-transition table reproduces.** My PSS by mix (t=0 / 200 / 1000 / 1800 / 2000):
    video/image 252 / 382 / 515 / 621 / 606 (ADR: 420 / 381 / 528 / 632 / 606). Videos only 247 / 322 / 355 / 377 / 335
    (ADR: 246 / 324 / 343 / 377 / 334). Images only 197 / 266 / 276 / 277 / 279 (ADR: 196 / 265 / 275 / 277 / 278).
    Videos only: `maxVideosInDom` 2, `maxLayers` 2, 2000 of 2000 pipelines created and destroyed, `errors` empty.
  - I didn't rerun the 240-transition strategy table. I checked it against the developer's raw files in
    `artifacts/spike-long/`, and they match the ADR (CSV `pipelinesCreated` is populated now).

## Findings
| # | Severity | Location | Finding | Expected |
|---|---|---|---|---|
| 1 | MAJOR | `docs/PLAYER_SPEC.md:118-121`, `docs/adr/0008-video-lifecycle.md:31-39`, `docs/BACKLOG.md:109-110` | The new daily reload breaks R-24 when the server can't be reached. The reload is triggered by time (20 h or 26 h of uptime), not by the server, so it will also fire while the network or server is down. `/screen` is served with `Cache-Control: no-cache` (ADR-0007:50) and there's no service worker, so `location.reload()` with the server unreachable puts Chromium's network error page on the screen. Nothing on that page retries, so the screen stays on it until a person intervenes. ADR-0001 explicitly rejected "show an error page" for offline. "The reload starts from the cached manifest" assumes the page itself loads, and it won't. (The `appVersion` reload doesn't have this problem, because it only happens just after the server answered.) | Spec the guard. Reload only after a fresh check that the server is reachable, for example a successful `no-cache` request for `/screen` (or the manifest) made right before reloading. If the check fails, postpone and try again at a later transition. Put this in PLAYER_SPEC §9 and ADR-0008, and add a WP-08 acceptance item: "server stopped when the daily reload is due → the player keeps cycling and reloads once the server is back". |
| 2 | MINOR | `docs/PLAYER_SPEC.md:118-120` | The reload only happens "at the next transition", but some screens never transition: single video mode (R-14, a video looping for weeks, which is the most video-heavy case), a playlist with one playable slide (§2: "do not transition to itself") and the empty state. For those, the safety net never fires. | Define the trigger when there are no transitions, e.g. at a loop boundary in single video mode and at any time in the empty state or with a single slide. Apply the finding 1 guard there too. |
| 3 | MINOR | `docs/BACKLOG.md:158-160`, `docs/adr/0008-video-lifecycle.md:43-45` | The WP-13 soak follow-up doesn't do what round 1 asked for (≥1000 video plays and a threshold that can detect the problem). `timeScale` scales the player's timing constants (ARCHITECTURE:195), not video playback, so 30 minutes with real-length clips gives a few hundred video plays, not "thousands of transitions". And "more than 150 MB between the first and the last third" would pass the exact rate measured here: ≈0.11 MB per transition, so ≈70–145 MB between the thirds depending on how many transitions the soak manages. The instrument is also suspect: in the 240-run, release grows ≈1.2 MB per transition (320→578), while the 2000-run grows ≈0.11, yet the total growth per run is about the same (≈250 vs ≈225 MB) with a similar number of samples (12 vs 10). So the forced GC and the CDP `Media` domain (which buffers every media log event) may be part of what's measured. "Same method as the spike" carries that over. | State a minimum number of video plays for the soak (e.g. ≥1000, with short clips), and express the pass threshold as projected growth per day that fits the device memory budget alongside the daily reload, not as a fixed 150 MB. Sample without `Media.enable`, or run one A/B, so the instrument isn't part of the measurement. |
| 4 | NIT | `docs/adr/0008-video-lifecycle.md:33` | "Slowing from +147 MB in the first half to +78 MB in the second" measures the first half from t=200 and the second half to t=2000. The t=2000 sample is taken after the loop has stopped (videos only: 377→335 once the next video is no longer prepared). From t=200→1000 vs 1000→1800 it's +147 vs +104 in your data and +133 vs +106 in mine. Either way it's weak evidence of slowing. The ADR rightly says a plateau isn't proven. | Drop "slowing", or compute both halves over equal spans and leave out the post-run sample. |
| 5 | NIT | `tests/Signage.E2E/spikes/video-lifecycle/harness.js:128` | Round 1 asked whether the next video reaches `canplay` before its turn. `transition()` silently waits up to 3 s and doesn't record whether the cap was hit. On the localhost nginx this is almost certainly fine, but the result isn't recorded. | Count the times `ready` lost the race (one line), or say in the ADR that it wasn't measured. |

## Requirement check
| R-ID / criterion | Met? | Evidence (test name or file:line) |
|---|---|---|
| Throwaway harness in `tests/Signage.E2E/spikes/`, kept but not shipped | Yes | `tests/Signage.E2E/spikes/video-lifecycle/`, served only by the e2e `testmedia` nginx |
| Cycles videos and images with the planned transition | Yes | harness (300 ms transition, stated in the ADR); three mixes (`harness.js:11-21`) |
| 200+ transitions under different strategies | Yes | `VideoLifecycleSpike.Measure` × 4 × 240; `MeasureLong` × 3 × 2000 (reproduced) |
| Openly licensed media from a local container, no large binaries | Yes | `scripts/generate-test-media.sh`; nothing binary in the diff |
| PLAYER_SPEC §4: release hypothesis | Yes | ADR table: 120/120 pipelines destroyed at cover time, lowest PSS |
| PLAYER_SPEC §4: reuse vs. fresh | Yes | ADR "Why not reuse" |
| PLAYER_SPEC §4: memory trend over 200+ transitions | Yes | Measured with tracing off and equal sampling. Growth recorded honestly as unproven to plateau, with a projection (ADR:31-39) |
| PLAYER_SPEC §4: preloading the next video vs. releasing the current one | Yes | Videos-only mix: max 2 `<video>`, 2000/2000 destroyed (reproduced). Order stated in `PLAYER_SPEC.md:57-59` |
| Raspberry Pi implications from the literature | Yes | ADR §Consequences |
| Output ADR with data, recommendation and decision; PLAYER_SPEC updated | Partly | ADR-0008 and PLAYER_SPEC §4 are good. The new §9 daily reload needs the offline guard (finding 1) |
| R-22 (resource handling decided by the spike, in an ADR) | Yes | ADR-0008 |
| R-24 (offline resilience), affected by the new §9 text | No | Finding 1 |

## Previous round follow-up
| Round 1 # | Status | Notes |
|---|---|---|
| 1 MAJOR (growth/instrument/plateau) | Addressed, with a new problem | Tracing is off (`VideoLifecycleSpike.cs:18`, `E2ETest.cs:28`). Equal sampling across mixes, 2000-transition runs, and the growth is recorded honestly with a MB/day projection (option b). The mitigation (daily reload) introduces finding 1. The soak follow-up is only partly what was asked (finding 3). |
| 2 MAJOR (back-to-back videos) | Fixed | `mix=videos`, measured and reproduced. The order is stated in PLAYER_SPEC §4 and the WP-08 backlog line. |
| 3 MINOR (`playersCreated` = 0) | Fixed | Counted from `kWebMediaPlayerCreated` (`VideoLifecycleSpike.cs:69-72`). The CSVs agree with the ADR. |
| 4 NIT ("Players" wording) | Fixed | "Pipelines" / "media-pipeline". |
| 5 NIT (forced-GC caveat, codec probe) | Fixed | ADR:63-67. |

## Notes for the product owner
- Releasing covered videos is clearly right and reproducible. Back-to-back videos are safe: never more than two
  `<video>` elements, and every pipeline is freed.
- A mix of videos and images still grows by about 0.1 MB per transition, with no proven plateau. In a worst case
  (10 s slides, every other one a video) that's ≈0.7–0.8 GB a day. The developer chose a **daily self-reload** to bound
  it: a short black moment, normally between 02:00 and 05:00, but at any time if a screen has run 26 h without one.
  That's a visible behaviour you may want to confirm. It also needs the offline guard in finding 1 before it's safe.
- On a 2 GB Pi, one day's growth at that worst-case rate is a real share of memory. WP-13's soak, once calibrated
  (finding 3), is where that gets checked.
