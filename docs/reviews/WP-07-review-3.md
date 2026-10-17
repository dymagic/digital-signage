# Review WP-07 — round 3
- Branch: wp-07-video-spike @ 86df161
- Verdict: CHANGES_REQUIRED
- Verified:
  - `git diff 32e84ce..86df161 --stat`: since round 2 only docs changed (BACKLOG, PLAYER_SPEC, ADR-0008, PROGRESS, plus
    the round-2 review). No code, harness or test changes.
  - `dotnet build`: succeeded, 0 warnings, 0 errors.
  - `dotnet test`: Passed, total 391 (the E2E tests skip outside Docker, as before).
  - `scripts/test-e2e.sh`: exit 0. Signage.E2E total 21, failed 0, skipped 8 (all spike tests, gated as intended).
  - I didn't rerun the spike measurements (the harness hasn't changed since round 2, when I reproduced the 2000-transition
    table). I recomputed the ADR's rates from its own table and from `artifacts/spike-long/video-release-mixed-2000.csv`.
  - I can't independently verify the product owner's confirmation, because it's recorded only in developer-written docs.
    I take ADR-0008:31 at face value and flag it below.

## Findings
| # | Severity | Location | Finding | Expected |
|---|---|---|---|---|
| 1 | MAJOR | `docs/adr/0008-video-lifecycle.md:32-36`, `docs/BACKLOG.md:163-165` | The headline growth rate for the video/image mix is understated by about 40 %, and two things are built on it. "About 0.09 MB per transition over 2000 transitions" is (606 − 420) / 2000, so it uses both endpoints that the ADR itself calls unreliable. t=2000 was sampled after the loop stopped (the ADR's own note), and t=0 is an outlier: 420 MB, *higher* than t=200 (381), while the other mixes start at 196–246 and my round-2 rerun started at 252. The same sentence then gives +147 MB (200→1000) and +104 MB (1000→1800), which is 251 MB / 1600 = **0.157 MB per transition**. My reproduction gives 0.149. The consequences: (a) the worst-case projection "about 0.7 GB a day" (8,640 × 0.09) is really about **1.3 GB a day** if the growth is linear. That's a material difference on a 2 GB Pi that reloads only once a day, and it's the figure the product owner confirmed the reload on. (b) The WP-13 soak threshold "2× the spike's rate (0.09 × 2)" = 0.18 MB per transition is about the measured early rate (200→1000: 0.184 MB per transition). The soak measures from the end of the first third, which is the early, faster part, so the "2× regression guard" is really ≈1× and will likely fail on correct behaviour. (I quoted ≈0.11 in round 2 and missed this. It's corrected here.) | Restate the rate from the equal-span data (≈0.15 MB per transition, t=200→1800, excluding t=0 and t=2000) and the linear worst case (≈1.3 GB a day). Set the WP-13 threshold from the corrected rate (e.g. 2 × 0.15 = 0.30 MB per transition). Tell the product owner the corrected number, and say in the ADR whether once a day is still enough for a 2 GB Pi in the worst case, or whether the interval should be a `config.js` value that can be shortened. |
| 2 | MINOR | `docs/PLAYER_SPEC.md:119-123` | The offline guard (round 2, finding 1) is now specified, which is good, but two gaps leave the original failure mode partly open. (a) For a carousel, the reachability check runs on the once-a-minute timer, while the reload happens "at its next transition". That can be minutes later (a long video, up to the 10-min cap), and the server may be gone by then. Round 2 asked for the check right before reloading. (b) "The server answers" doesn't say *with a success status*. During a server restart behind a reverse proxy, `GET /screen` gets a 502/503 from the proxy. If that counts as "answers", the reload puts the proxy's error page on the screen, and nothing on that page retries. | Say that the check must succeed (`response.ok`) and must be made immediately before the reload: for a carousel, start the check shortly before the due transition (or at it) and reload only if it succeeded within the last few seconds. Otherwise keep cycling and try again at the next check. |
| 3 | MINOR | `docs/BACKLOG.md:110-112` vs `docs/ARCHITECTURE.md:194-196` | WP-08's daily-reload E2E tests are specified as "test-only clock values via `?test=1` overrides". ARCHITECTURE allows exactly one test hook (`?timeScale`, with `?test=1`) and says "No other test-only branches in production code". As written, WP-08 would either break that rule or leave the doc untrue. | Prefer Playwright's Clock API (`Page.Clock`: install, fast-forward or set the system time; it controls `Date`, timers and `performance.now`), which needs no production branch. Otherwise, update ARCHITECTURE:194-196 to list the new hook. |
| 4 | NIT | `docs/BACKLOG.md:160-162`, `docs/adr/0008-video-lifecycle.md:46` | "About 1,000–1,400 transitions" in 30 min looks optimistic. The clips are 3, 3 and 4 s at normal speed, and images and transitions are scaled by 0.05. That's ≈3.9 s per video+image pair, so ≈900 transitions (≈460 video plays). Also, the spike baseline was measured *with* the CDP Media domain enabled, while the soak runs without it, so the two rates aren't measured the same way. | Re-estimate the count (or state a minimum and let the soak run until it's reached), and note the Media-domain difference next to the threshold. |

## Requirement check
| R-ID / criterion | Met? | Evidence (test name or file:line) |
|---|---|---|
| Throwaway harness in `tests/Signage.E2E/spikes/`, kept but not shipped | Yes | `tests/Signage.E2E/spikes/video-lifecycle/`, served only by the e2e `testmedia` nginx (unchanged since round 2) |
| Cycles videos and images with the planned transition | Yes | Harness with three mixes. Spike tests gated (`VideoLifecycleSpike.cs:47`) |
| 200+ transitions under different strategies | Yes | `Measure` × 4 × 240; `MeasureLong` × 3 × 2000 (reproduced in round 2) |
| Openly licensed media from a local container, no large binaries | Yes | `scripts/generate-test-media.sh`. Nothing binary in the diff |
| PLAYER_SPEC §4: release hypothesis / reuse vs fresh / preload vs release | Yes | ADR-0008 Decision and Data. PLAYER_SPEC §4 |
| PLAYER_SPEC §4: memory trend over 200+ transitions | Partly | Measured and reproducible, but the summarised rate and projection are wrong (finding 1) |
| Raspberry Pi implications from the literature | Yes | ADR-0008 Consequences |
| Output ADR with data, recommendation and decision; PLAYER_SPEC updated | Partly | ADR-0008 and PLAYER_SPEC §4/§9. Rate correction pending (finding 1) |
| R-22 (resource handling decided by the spike, in an ADR) | Yes | ADR-0008 |
| R-24 (offline resilience) under the daily reload | Mostly | PLAYER_SPEC.md:120-121 guard. Residual window and status check (finding 2) |

## Previous round follow-up
| Round 2 # | Status | Notes |
|---|---|---|
| 1 MAJOR (daily reload vs R-24) | Fixed, with a residual gap | Reachability check and postpone are in PLAYER_SPEC §9, ADR-0008 and the WP-08 acceptance tests. The check timing and status check are in finding 2 (MINOR). |
| 2 MINOR (screens that never transition) | Fixed | One slide, the empty state and single video mode reload at the check (PLAYER_SPEC.md:122-123). |
| 3 MINOR (soak calibration) | Partly | It's now framed honestly as a regression guard, without the Media domain, with a rate-based threshold. That's acceptable instead of ≥1000 video plays. But the threshold rests on the wrong rate (finding 1), and the count estimate is high (finding 4). |
| 4 NIT ("slowing") | Fixed | Equal 800-transition spans, last sample excluded (ADR-0008:32-34). |
| 5 NIT (`canplay` race not recorded) | Fixed | ADR-0008 says it wasn't measured and that WP-09 covers late or failed videos. |

## Notes for the product owner
- The daily reload you confirmed was presented with a worst case of about 0.7 GB of growth a day. From the spike's own
  data, the realistic worst-case figure is about **1.3 GB a day**, if growth stays linear (10 s slides, every other one
  a video). The growth seemed to slow in the second half, but that isn't proven. On a 2 GB Pi, one day of that is a lot.
  Once the developer corrects the numbers, you may want to decide whether the reload interval should be configurable.
- The confirmation itself is recorded only in the developer's docs (ADR-0008 and PROGRESS). If that's not what you
  agreed, say so.
- Everything else from the spike holds: releasing covered videos is clearly right, and back-to-back videos never use
  more than two `<video>` elements.
