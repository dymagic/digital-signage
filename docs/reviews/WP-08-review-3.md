# Review WP-08 — round 3
- Branch: wp-08-player-core @ 13ec331
- Verdict: APPROVE
- Verified:
  - `dotnet build`: Build succeeded, 0 warnings, 0 errors.
  - `dotnet test`: total 429, succeeded 390, failed 0, skipped 39 (the E2E project skips outside Docker; one more than round 2 because of the new E2E test).
  - `scripts/test-e2e.sh`: Signage.E2E total 39, errors 0, failed 0, skipped 8 (the opt-in WP-07 spike measurements), exit 0.
  - I read the fix commit `13ec331` in full and the whole of `carousel.js` after it. I also checked `player.js` (where `stop()` is called) and `empty.js`. I traced these paths by hand: the single slide edited and ready, the single slide edited but failing (the old version stays and a calm 10 s retry follows, with no hot loop), an edit that only changes the duration, a stale current slide with every alternative failing, `advance` returning false for each of its reasons (stopped, pending adopted to empty, pending adopted to the same single slide, wrap-around to the current slide, attempts exhausted), and `sameContent` with the id included in `adoptPending`.

## Findings
| # | Severity | Location | Finding | Expected |
|---|---|---|---|---|
| 1 | MINOR | `wwwroot/screen/carousel.js:128-140`; `docs/PLAYER_SPEC.md` §2 | The new rule is untested: "a stale current slide (expired or removed) is dropped to the empty state when nothing else can be shown". It is user-visible behaviour, it is now in the spec, and no E2E or Clock test covers it. The logic looks right. `isStale()` is only true when the list is non-empty and the current id is missing, so the other `advance() === false` cases (wrap-around, a single same slide, an empty list) correctly do not trigger it. | Add a Clock-driven E2E test: two slides, the second one's host is broken, the first one's schedule ends during its dwell, and the screen ends up in `data-mode=empty`. Then it recovers when the second one loads. This may be deferred as a BACKLOG follow-up line. |
| 2 | NIT | `tests/Signage.E2E/Player/CarouselTests.cs` (`An_edit_to_the_only_slide_…`, the "settles" check) | The check that nothing more happens waits 1.5 s, but the manifest is polled every 6 s at this time scale. The next poll, which would return the already-edited content, usually arrives after the window. So the check that "an unchanged edited slide doesn't swap again" rarely runs. The unit of logic (`sameContent` before `prepare`) is simple, so the risk is low. | Wait for one more manifest poll, e.g. until `polls` has gone up by one, before you assert that the activation count is still 2. |
| 3 | NIT | `carousel.js:128-132` (and the same pre-existing pattern at `:88-93`) | `advance()` also returns false when the carousel was stopped while it was waiting on `isReady`. If the current slide is stale at that moment, `showEmptyState()` prepends the empty state to the stage. `stop()` has just handed that stage to the pairing UI or the preview message (`player.js:81-86,119-121`). This needs a very unlikely race. | Guard both empty-state fallbacks with `!this.stopped`. |
| 4 | NIT | `docs/PLAYER_SPEC.md` §2 | The "Skipping" bullet still says "If nothing else can be shown, the current slide stays for another round". The exception for an expired or removed slide appears only at the end of the one-slide bullet, so a reader of the Skipping rule misses it. | Move the exception sentence into the Skipping bullet, or add a cross-reference. |

## Requirement check
| R-ID / criterion | Met? | Evidence (test name or file:line) |
|---|---|---|
| Manifest endpoint with ETag / 304, weak comparison | Yes | `ManifestTests` (unchanged since round 2) |
| E2E: order follows the resolved sequence (R-12) | Yes | `CarouselTests.Slides_play_in_the_resolved_sequence_and_loop` |
| E2E: outgoing stays still, incoming animates, shadow only while animating (R-20) | Yes | `CarouselTests.The_incoming_slide_slides_over_…`; `player.css` |
| Progress bar over the duration, no double dwell (R-21) | Yes | `CarouselTests.The_progress_bar_fills_…`, `A_new_manifest_during_a_slide_…` |
| At most 3 layers, ADR-0008 release order | Yes | `__maxLayers` assertion; the single-slide edit path has at most 2 layers (`carousel.js:100-108`), and the old layer is released in `transition` before anything else is prepared |
| Image / WebPage / Html slides; Html sandbox `allow-scripts` only | Yes | `SlideTypeTests.Images_and_web_pages_play_…`; `slides/html.js` |
| Preloading + grace; skipping without a second dwell | Yes | `carousel.js:160-182` |
| Empty state (R-23), including "nothing can be shown" with a calm retry | Yes | `CarouselTests.A_screen_without_content_…`; `SlideTypeTests.A_screen_whose_slides_all_fail_…` (now Clock-driven) |
| Single slide: no transition to itself, no progress; **edits shown without a reload (R-25 fallback)** | Yes | `carousel.js:97-115`; `CarouselTests.An_edit_to_the_only_slide_of_a_screen_shows_up_without_a_reload` (asserts the new srcdoc and the same page, with no reload) |
| Offline cycling + cached start (R-24, R-41) | Yes | `CarouselTests.It_keeps_cycling_…`, `A_kiosk_opened_with_its_screen_address_…` |
| Schedule re-check on the client (R-03) | Yes; the stale-to-empty fallback is untested (finding 1) | `ClockTests.An_offline_screen_stops_showing_a_slide_when_its_schedule_ends`; `carousel.js:128-140` |
| Admin preview (R-13), Editor+, 401/403/404 | Yes | `ManifestTests` preview tests; `CarouselTests.Previewing_…` |
| Demo seed, multi-replica safe | Yes | `DemoSeedTests` |
| Daily reload | Yes | `ClockTests` (4 tests) |
| Security (sandbox, `textContent`, preview authz) | Yes | unchanged; no new `innerHTML` with data (the `empty.js` template is static) |
| Docs truthful | Yes (see NIT 4) | PLAYER_SPEC §2 |

## Previous round follow-up
| Round-2 # | Status | Evidence |
|---|---|---|
| 1 MAJOR edits to a one-slide screen never shown | Fixed | `carousel.js:100-110`: the edited version is prepared, then slides in once ready. If it fails, the old version stays with a 10 s retry. `current.slide` is refreshed for duration-only changes. PLAYER_SPEC §2 documents it. New E2E test passes. |
| 2 NIT `sameContent` ignores the id | Fixed | `carousel.js:17` includes `id`. A same-content different slide is now re-prepared, so `data-slide-id` is correct. |
| 3 NIT stale slide kept when nothing else loads | Fixed | `carousel.js:128-140` `isStale()` leads to the empty state and a retry. It is untested (finding 1). |
| 4 NIT sleep-based calm-retry assertion | Fixed | `SlideTypeTests` now drives the clock with `Clock.PauseAtAsync`/`RunForAsync`. It asserts no request before 5 s and a retry after. The remaining real-time waits only let network events arrive. |

## Notes for the product owner
- All four round-2 items are addressed. A one-slide screen (such as a "menu of the day") now shows edits within one manifest poll, and you don't need to wait for the daily reload. The remaining items are small: one missing test for a rare fallback, plus polish. They can go to the backlog as follow-ups.
