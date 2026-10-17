# Review WP-07 — round 4
- Branch: wp-07-video-spike @ 6c82cc5
- Verdict: APPROVE
- Verified:
  - `git diff 86df161..6c82cc5 --stat`: since round 3 only docs changed (BACKLOG, PLAYER_SPEC, ADR-0008, PROGRESS, and
    the round-3 review). No code, harness or test changes. Working tree clean.
  - `dotnet build`: succeeded, 0 warnings, 0 errors.
  - `dotnet test`: Passed, total 391.
  - `scripts/test-e2e.sh`: exit 0. Signage.E2E total 21, failed 0, skipped 8 (all spike tests, gated as intended).
  - Recomputed the ADR figures from `artifacts/spike-long/video-release-mixed-2000.csv` (git-ignored, present locally):
    381 → 528 → 632 MB at t=200/1000/1800, i.e. +147 and +104 MB, 0.184 and 0.130 MB per transition, mean 0.157.
    0.157 × 8,640 ≈ 1.36 GB a day. The ADR's "≈0.15 MB per transition" and "≈1.3 GB a day" match. The WP-13 threshold
    is now 2 × 0.15 = 0.30 MB per transition, which leaves real headroom above the measured early rate (0.18).
  - Checked that Playwright .NET 1.63 (`Directory.Packages.props:23`) has the Clock API (`IPage.Clock`, since 1.45),
    so the WP-08 test plan is feasible without a production test branch.
  - The product owner's second confirmation is recorded only in developer-written docs (ADR-0008:31). I take it at face
    value, as in round 3.

## Findings
| # | Severity | Location | Finding | Expected |
|---|---|---|---|---|
| 1 | NIT | `docs/adr/0008-video-lifecycle.md:36-43` | The ADR now gives the right worst case (≈1.3 GB a day) but doesn't say outright whether that fits. With a ≈0.4 GB starting PSS, one day's worth is ≈1.7 GB on a 2 GB Pi, and the figures come from x86 software decoding, not a Pi. "The figure the daily reload must hold down" reads as if the reload lowers it, but a once-a-day reload only caps it at one day's growth. PLAYER_SPEC.md:126 does say all values live in `config.js`, so the interval can be shortened. | One sentence in the ADR: the worst case is tight on a 2 GB Pi, the interval is a `config.js` value, and WP-13 or a real-device run should confirm it. |
| 2 | NIT | `docs/adr/0008-video-lifecycle.md:39` | "Reloads only when the server answers at that moment" is looser than PLAYER_SPEC.md:121-124 ("immediately before reloading", "only on a 2xx response"). PLAYER_SPEC is the normative text, so this is only a wording mismatch. | Say "answers with 2xx" in the ADR, or point to PLAYER_SPEC §9. |

## Requirement check
| R-ID / criterion | Met? | Evidence (test name or file:line) |
|---|---|---|
| Throwaway harness in `tests/Signage.E2E/spikes/`, kept but not shipped | Yes | `tests/Signage.E2E/spikes/video-lifecycle/`, served only by the e2e `testmedia` nginx |
| Cycles videos and images with the planned transition | Yes | Harness with three mixes. Spike tests gated (`VideoLifecycleSpike.cs:47`) |
| 200+ transitions under different strategies | Yes | `Measure` × 4 × 240; `MeasureLong` × 3 × 2000 (reproduced in round 2; raw CSVs present) |
| Openly licensed media from a local container, no large binaries | Yes | `scripts/generate-test-media.sh`. No binaries in the diff; `artifacts/` is git-ignored (`.gitignore:18`) |
| PLAYER_SPEC §4: release hypothesis / reuse vs fresh / preload vs release | Yes | ADR-0008 Decision and Data. PLAYER_SPEC §4 |
| PLAYER_SPEC §4: memory trend over 200+ transitions | Yes | ADR-0008 Data and the corrected rate (ADR-0008:32-37), consistent with the CSV |
| Raspberry Pi implications from the literature | Yes | ADR-0008 Consequences |
| Output ADR with data, recommendation and decision; PLAYER_SPEC updated | Yes | ADR-0008; PLAYER_SPEC §4/§9 |
| R-22 (resource handling decided by the spike, in an ADR) | Yes | ADR-0008 |
| R-24 (offline resilience) under the daily reload | Yes (spec) | PLAYER_SPEC.md:118-126: 2xx check immediately before the reload, postpone otherwise. WP-08 acceptance tests (BACKLOG.md:110-112) |

## Previous round follow-up
| Round 3 # | Status | Notes |
|---|---|---|
| 1 MAJOR (rate understated, projection and WP-13 threshold) | Fixed | Rate ≈0.15 MB per transition from the equal spans with both unreliable endpoints excluded; ≈1.3 GB a day; WP-13 threshold 2 × 0.15 in BACKLOG.md:163-165 and ADR-0008:47-48. The product owner reconfirmed (ADR-0008:31). Explicit headroom statement: NIT 1. |
| 2 MINOR (offline guard timing and status) | Fixed | PLAYER_SPEC.md:120-124: check made immediately before reloading, 2xx only, 502 during a restart named explicitly. |
| 3 MINOR (`?test=1` overrides vs ARCHITECTURE §6) | Fixed | BACKLOG.md:110-111 uses Playwright's Clock API instead; no new production hook. |
| 4 NIT (transition count, Media domain) | Fixed | "Roughly 900 transitions"; Media-domain difference noted next to the threshold (BACKLOG.md:161-163). |

## Notes for the product owner
- The reload decision now rests on the right number: in the worst case (10 s slides, every other one a video) a
  browser could grow by about 1.3 GB in a day before it reloads. That was measured on a PC, not a Pi. On a 2 GB Pi it is
  tight but bounded. The reload interval is a player config value, so it can be shortened if real devices need it.
- Your reconfirmation is recorded in ADR-0008 by the developer. If that isn't what you agreed, say so.
