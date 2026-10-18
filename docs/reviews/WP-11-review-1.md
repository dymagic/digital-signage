# Review WP-11 — round 1
- Branch: wp-11-identification @ a0de8b8
- Verdict: APPROVE
- Verified:
  - `git diff main...wp-11-identification --stat`: 27 files, +481/−27; full diff read.
  - `dotnet build`: Build succeeded, 0 warnings, 0 errors.
  - `dotnet test`: Passed, 412 succeeded, 0 failed (E2E project skipped outside Docker as designed).
  - `scripts/test-e2e.sh`: exit 0. Signage.E2E Total 61, Failed 0, Skipped 8 (only the opt-in `Spikes.*`). All four
    `IdentifyTests` ran and passed.
  - Looked at `artifacts/e2e/screens/player-identify.png` (24 px red inset border, the name on a dark pill, centred) and
    `player-info-overlay.png` (`Info 32f64f · 172.18.0.5` bottom-left, small and dim, clear of the progress bar).

## Findings
| # | Severity | Location | Finding | Expected |
|---|---|---|---|---|
| 1 | MINOR | `src/Signage.Web/wwwroot/screen/reload.js:52-60` | The WP-10 carry-over listed in this package (take back a version reload handed to a carousel that stops cycling) is implemented but no test covers it. It changes the R-26 reload path, which is easy to break without anyone noticing. | Add an E2E test: a 2-slide carousel gets a new `appVersion`, a pushed manifest drops it to one slide (or single video) before the next transition, and the clock is fast-forwarded one check. The `/screen` navigation must happen. Or record the gap as a follow-up in BACKLOG. |
| 2 | MINOR | `src/Signage.Web/wwwroot/screen/player.css:150-159`, `overlay.js` `setOnline` | The offline dot in the info overlay (PLAYER_SPEC §7 and §9) is implemented but not tested. | Extend `The_info_overlay_appears_and_disappears_live_with_the_global_setting` (or add a test) that aborts the manifest route and expects `.info-overlay.offline`. |
| 3 | MINOR | `Pages/Admin/Screens/Detail.cshtml.cs:118`, `Index.cshtml.cs:31`, `Features/Events/PostgresScreenNotifier.cs:57` | When `pg_notify` fails, the identify is lost silently: the admin still sees "The screen now shows a red border…" and the warning log says "they pick the change up with their next poll". That is false for identify events, because they are not part of the manifest. The success message also shows for a screen that is offline or has no open stream. | Make the log text depend on the event kind (or make it neutral). Soften the status text, for example "Sent: an online screen shows…", or report the failure. Small, but it is what an operator sees when diagnosing problems. |
| 4 | MINOR | `docs/PLAYER_SPEC.md:13` vs `:125` and `player.css:78,140`; `docs/ARCHITECTURE.md:124-125` | The docs disagree with each other. §1 says "slides → info overlay → progress bar → identify overlay", but the code and the new §7 text put the info overlay (z 25) above the progress bar (z 20). There is no visual overlap, so this has no user-visible effect. ARCHITECTURE still says "htmx partials for … identify", but identify is a plain form POST with a redirect. | Update §1's layer order (or set the info overlay's z-index below 20) and the ARCHITECTURE line, so `docs/` stays truthful. |
| 5 | NIT | `tests/Signage.E2E/Player/IdentifyTests.cs:106` | The info overlay test switches a **global** setting while other E2E tests run in parallel. This already forced the `SlideTypeTests` workaround (blocking the event stream). Any future test that counts manifest pushes or takes a screenshot can be disturbed the same way. | Consider running the setting-toggling tests in a non-parallel collection. |
| 6 | NIT | `IdentifyTests.cs` | The 1 Hz opacity pulse (100 % ↔ 55 %) and the 500 ms fade are not asserted, only the border colour and width. | Optional: assert `animationName`/`animationDuration` of `::before`. |
| 7 | NIT | `Detail.cshtml.cs:24` | The type is written fully qualified (`Signage.Web.Features.Events.IScreenNotifier`) where a `using` would read better (`Index.cshtml.cs` uses one). | Add a `using`. |
| 8 | NIT | `connection.js:61`, `overlay.js:43` | The fallback `30` is hard-coded rather than taken from `config.js` ("all timing constants live in one config.js"). An identify that arrives before any manifest would show an empty pill. | Move the default to `config.js`, and skip the pill (or show only the border) while `name` is empty. |

## Requirement check
| R-ID / criterion | Met? | Evidence (test name or file:line) |
|---|---|---|
| R-30 Info overlay, global toggle, server-observed IP | Yes | `IdentifyTests.The_info_overlay_appears_and_disappears_live_with_the_global_setting` (asserts `Name · a.b.c.d`, appears and disappears live within 5 s without a transition); `overlay.js` `update`; IP from `http.ObservedIp()` (`PlayerApi.cs:96`); preview passes `null`, so it shows the name only |
| R-31 Identify one: thick pulsing red border + name, 30 s | Yes | `IdentifyTests.Identify_shows_a_pulsing_red_border_and_the_name_and_a_new_identify_restarts_the_timer` (border `rgb(225,29,72)` 24 px, name, ends after the scaled 30 s); integration `LiveUpdateTests.Identify_reaches_only_that_screen_and_identify_all_reaches_every_screen_with_30_seconds` (only that screen, `{"seconds":30}`) |
| R-32 Identify all: name, no border, 30 s | Yes | `IdentifyTests.Identify_all_shows_every_screens_name_without_the_border`; same integration test (both streams receive `identify-all`) |
| Accept: timer restart | Yes | The restart test checks the state at first+7 s (past the first run) and that the run lasts > 5.5 s after the second click; `overlay.js:43-57` clears both timers |
| Accept: overlay appears live | Yes | Info overlay test above; `overlays.update` is called on every manifest, not at a transition |
| Overlays above single video (PLAYER_SPEC §6/§7) | Yes | `IdentifyTests.Identification_shows_over_single_video_mode` |
| Authz: Editor+ may identify (BRIEF §3 matrix), anonymous may not | Yes | Integration test signs in as an editor; `Identifying_needs_a_signed_in_user_and_an_existing_screen` (anonymous → login redirect, deleted screen → list); folder convention `/Admin` = Editor; Razor Pages antiforgery on both forms |
| Multi-replica | Yes | Identify goes through `pg_notify` → `ScreenEventListener` → per-replica hub, like `manifest-changed`; no new in-memory state |
| WP-10 NIT: stray `)` in R-13 comment | Yes | `PlayerApi.cs:116` |
| WP-10 NIT: signal from route handler instead of a `Task.Delay` loop | Yes | `tests/Signage.E2E/Player/LiveUpdateTests.cs` `firstAttempt` TCS |
| WP-10 NIT: take the version reload back from a carousel that stopped cycling | Implemented, untested | `reload.js:52-60` (Finding 1) |
| Glossary / scope | Yes | No banned terms added; nothing out of scope |
| Localization | Yes | Four new `SharedResource.resx` keys; the player shows only the screen name and IP (no UI strings) |

## Notes for the product owner
- Identification is fire-and-forget over the live event stream. A screen that is offline (or reconnecting) at the moment
  of the click doesn't show it later, and the admin message doesn't say so (Finding 3). That is a reasonable design,
  but worth knowing when you walk the office pointing at screens.
- The info overlay shows the IP the server sees. Behind NAT or a proxy that's the forwarded/public address, as R-30
  intends, not necessarily the device's LAN address.
