# Review WP-13 — round 1
- Branch: wp-13-acceptance @ 4f7f2d2
- Verdict: APPROVE
- Verified:
  - `git diff main...wp-13-acceptance --stat`: 21 files, +435/−19; full diff read.
  - `dotnet build`: Build succeeded, 0 warnings, 0 errors.
  - `dotnet test`: total 478, failed 0, succeeded 413, skipped 65 (the E2E project skips outside Docker by design).
  - `scripts/test-e2e.sh`: Total 65, Errors 0, Failed 0, Skipped 10 (8 spike measurements, README screenshots, soak).
  - `scripts/test-e2e.sh -class Signage.E2E.Player.IdentifyTests -class Signage.E2E.Player.LiveUpdateTests` twice
    to check the new tests for flakiness: 11/11 passed both times.
  - `SIGNAGE_E2E_SOAK=1 scripts/test-e2e.sh -class "Signage.E2E.Soak.PlayerSoak"`: passed in 1820 s. My run:
    900 transitions in 30.3 min; PSS 344 → 595 (at 300) → 724 MB; growth 130 MB over 600 transitions =
    **0.216 MB/transition** (allowed 180 MB); max 2 layers, 1 `<video>` at every sample. This reproduces the
    developer's figures (0.215 and 0.230).
  - Scripted check of `docs/M1_ACCEPTANCE.md`: every R-ID in PROJECT_BRIEF has a row. Every test method named exists,
    in the class and project (U/I/E) the doc gives. All 19 "(n)" test counts match the number of `[Fact]`/`[Theory]`
    in the class.

## Findings
| # | Severity | Location | Finding | Expected |
|---|---|---|---|---|
| 1 | MINOR | `tests/Signage.E2E/Soak/PlayerSoak.cs:84-88`; `docs/M1_ACCEPTANCE.md` "Soak" | The soak passes its guard, but the shipped player grows about 45 % faster than the WP-07 spike (0.22 vs 0.15 MB per transition), and the growth looks steady rather than levelling off (my run: 595 → 617 → 658 → 663 → 701 → 731 → 724 MB). The test samples only total PSS, so nobody can tell whether this is a JS/DOM leak in the player (fixable) or Chromium media/native caches (only the reload helps). The PO is asked to choose a reload interval without that information. | Follow-up line in BACKLOG: record the JS heap at each sample as well (`Runtime.getHeapUsage` over the CDP session that is already open, after the forced GC). If the JS heap grows, look for retained slides or closures. Otherwise record that the growth is native and that the reload interval is the right tool. |
| 2 | NIT | `docs/M1_ACCEPTANCE.md` DoD row 5 | Points to `docs/reviews/MILESTONE-review-*.md`; the milestone review file is named `MILESTONE-M1-review-<n>.md`. | Fix the glob when the milestone review lands. |
| 3 | NIT | `docs/M1_ACCEPTANCE.md` "For the product owner" | "Open question … See `docs/PROGRESS.md`": the PO's acceptance document sends them to a working log for the actual question. | State the question and its options in one sentence here. |
| 4 | NIT | `tests/Signage.E2E/Soak/PlayerSoak.cs:44,47,87` | Elapsed time is measured with `DateTimeOffset.UtcNow`. The WP-00 follow-up (d) chose a `Stopwatch` for this, and a stopwatch can't be fooled by a wall-clock jump during a 30-minute run. | Use `Stopwatch.StartNew()`. |
| 5 | NIT | `src/Signage.Web/Features/Events/PostgresScreenNotifier.cs:41-65` | The new `false` return and the separate log text are only exercised through `RecordingScreenNotifier`; the real notifier's failure path has no test. | Optional: a small integration test with a data source whose database is stopped or unreachable, asserting `IdentifyAsync` returns false. |
| 6 | NIT | `README.md:118-119` | `docker run --rm -i postgres:16 psql "host=…"`: if the database is on the operator's machine (`localhost`), the container can't reach it without `--network host` (Linux) or `host.docker.internal`. | Add half a sentence about this. |

## Requirement check
| R-ID / criterion | Met? | Evidence (test name or file:line) |
|---|---|---|
| Write `docs/M1_ACCEPTANCE.md` (every R-ID → proof) | Yes | All 35 R-IDs present. Names, classes and counts checked by script (see Verified). Manual checks are labelled M and are honest about what was verified by hand and where (WP-12) |
| Close gaps: WP-11 #1 (version reload taken back when cycling stops) | Yes | E `LiveUpdateTests.A_version_reload_waiting_for_a_transition_happens_anyway_when_the_screen_stops_cycling`. It covers the `reload.js` `reloadSoon` watch: without the watch no transition comes in single video mode, so the test would fail. Uptime is far from the daily window, so the daily reload can't make it pass |
| Close gaps: WP-11 #2 (offline dot) | Yes | E `IdentifyTests.The_info_overlay_shows_a_dim_dot_while_the_screen_is_offline` (dot appears with `::after`, goes back online) |
| Close gaps: WP-11 #3 (identify failure is reported, log text corrected) | Yes | `Detail.cshtml.cs:118-126`, `Index.cshtml.cs:30-38` show `Screens.IdentifyFailed`. `PostgresScreenNotifier.cs:56-63` uses a log text that depends on the kind (`AllManifestsChanged` is also kind `manifest-changed`, so it correctly keeps the "next poll" text). I `ScreenPagesTests.Identify_says_so_when_the_screens_could_not_be_told` (both handlers). Localised (`SharedResource.resx`) |
| Close gaps: WP-11 #4 (docs) | Yes | PLAYER_SPEC.md:13-14 and §7 agree (info overlay 8 px above the bar); ARCHITECTURE.md:126 "plain form posts" |
| Close gaps: WP-11 NITs 5–8 | Yes | `GlobalStateTests` collection (`DisableParallelization`); pulse/fade asserted (`identify-pulse`, 0.5 s alternate infinite, 0.1 s fade at scale 0.2); `using` in `Detail.cshtml.cs`; `config.identifySeconds`; `nameEl.hidden` when the name is empty (`overlay.js:33,45`) |
| Close gaps: WP-12 NIT (psql without a local client) | Yes | README.md:118-119 (see NIT 6) |
| Run the full suite | Yes | Re-run by me, see Verified |
| 30-minute soak per ADR-0008 (PSS every 100 transitions, tracing off, no CDP Media, guard 2 × 0.15 MB/transition), numbers recorded | Yes | `PlayerSoak.cs` (`Tracing => false`; only `HeapProfiler.collectGarbage` on CDP; guard at lines 63-77). Numbers in M1_ACCEPTANCE and ADR-0008 Consequences, reproduced by my run. See MINOR 1 for the rate itself |
| ADR-0008 kept truthful | Yes | Consequences line added with the measured range |
| Request the milestone-level review | Pending | That is the next step after this package; DoD row 5 points to it |
| PROJECT_BRIEF §8 items 2, 3, 6 | Yes | dotnet test, E2E and M1_ACCEPTANCE verified above. Items 1, 4 and 5 are checked in the milestone review |
| Scope/glossary | Yes | No out-of-scope additions. No "monitor/card/feed" in the diff |
| Security | Yes | No new endpoints. Identify handlers keep their authz/antiforgery (unchanged attributes and folder convention). The new message is a resource string rendered through the layout's encoded TempData path |

## Notes for the product owner
- **Memory:** two developer runs and my own run all agree, at about 0.22 MB of browser memory per slide change, in
  headless Chromium with software decoding. At 10 s slides that is roughly 2 GB between the nightly reloads. Most
  screens with longer slides will see far less. If you plan to use 2 GB devices that play short video slides all day,
  either shorten `config.dailyReload` in `wwwroot/screen/config.js` or wait for the follow-up in finding 1. That
  follow-up tells whether the growth can be fixed in the player at all.
- **Cloud guides (R-62)** remain unrun against real Azure/AWS accounts, as the acceptance document says.
