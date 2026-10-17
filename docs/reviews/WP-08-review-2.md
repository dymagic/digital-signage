# Review WP-08 — round 2
- Branch: wp-08-player-core @ f30d6f4
- Verdict: CHANGES_REQUIRED
- Verified:
  - `dotnet build`: Build succeeded, 0 warnings, 0 errors.
  - `dotnet test`: total 428, succeeded 390, failed 0, skipped 38 (the E2E project skips outside Docker).
  - `scripts/test-e2e.sh`: Signage.E2E total 38, failed 0, skipped 8 (the opt-in WP-07 spike measurements), exit 0.
  - I read the round-2 fix diff (`git diff 03d89ec..f30d6f4`) in full, plus the whole of `carousel.js`, `player.js`,
    `reload.js` and the slide modules, and traced the new `advance`/`adoptPending`/`showFirst` paths by hand for these
    cases: a broken next slide, a changed manifest during a dwell, the current slide removed, an expired prepared slide,
    an empty list and a list where nothing can be shown.

## Findings
| # | Severity | Location | Finding | Expected |
|---|---|---|---|---|
| 1 | MAJOR | `wwwroot/screen/carousel.js:97-103` | **An edit to the only slide on a screen never reaches the screen.** When the playable list has one slide with the same id as `current`, the loop just idles. It never compares the new manifest's slide with `this.current.slide`. If an editor changes the HTML, URL or image of that slide, the manifest poll (60 s, already in this package) delivers the new version and it is saved to localStorage, but the screen keeps showing the old content until the daily reload, which can be up to 26 h later. The admin preview (R-13) of a one-slide screen has the same problem. A screen with a single info slide is a common office case, and this breaks the promise of R-25 even with the polling fallback. PLAYER_SPEC §9 covers multi-slide screens only ("takes effect at the next transition"), and a one-slide screen has no transitions. I missed this in round 1; the same code was there. | When the single slide's content changes (`!sameContent(list[0], this.current.slide)`), show the new version. Prepare it and transition (or swap it in place once it is ready), then update `this.current`. Write one sentence in PLAYER_SPEC §2/§9 saying which you chose. Add an E2E test: a one-slide screen whose slide is edited (or whose manifest is routed to new HTML) shows the new content without a reload. |
| 2 | NIT | `carousel.js:17,176-178` | `sameContent` ignores the id. When a *different* slide with identical content (the same image URL in two slides) takes the prepared slide's place, `candidate.slide` is swapped, but `layer.dataset.slideId` still holds the old id. The carousel itself uses `current.slide.id` and behaves correctly. Only the `data-slide-id` test hook (and so `__activations`) reports the wrong slide. | Also update `candidate.layer.dataset.slideId`, or include `id` in the comparison. |
| 3 | NIT | `carousel.js:138-159`, run loop `:105-116` | If the current slide has left the list (it expired or was removed) and every other slide fails to load, `advance` returns and the expired slide is dwelled on again, indefinitely. PLAYER_SPEC §2 says "if nothing else can be shown, the current slide stays", so this follows the spec. But for an expired slide it goes against R-03, and the new "nothing can be shown → empty state" rule would fit better. This is a rare combination. | Optional: when `current` is no longer in `playable(...)` and nothing else became ready, fall back to the empty state. |
| 4 | NIT | `tests/Signage.E2E/Player/SlideTypeTests.cs` (`A_screen_whose_slides_all_fail_…`) | The calm-retry assertion uses `WaitForTimeoutAsync(4000)` against a 5 s retry (at timeScale 0.5). It checks for an absence over a window, so a sleep is defensible, but the margin is 1 s of wall time. The Clock API would make it deterministic. | Optional: drive it with `device.Clock`. |

## Requirement check
| R-ID / criterion | Met? | Evidence (test name or file:line) |
|---|---|---|
| Manifest endpoint with ETag / 304, weak comparison | Yes | `ManifestTests.An_unchanged_manifest_answers_304_…`, `If_None_Match_uses_weak_comparison_and_lists` (3 cases); `PlayerApi.cs:112-120` |
| E2E: order follows the resolved sequence (R-12) | Yes | `CarouselTests.Slides_play_in_the_resolved_sequence_and_loop` |
| E2E: outgoing stays `translateX(0)`, incoming animates, shadow only while animating (R-20) | Yes | `CarouselTests.The_incoming_slide_slides_over_…` (now also asserts `box-shadow: none` on the waiting layer); `player.css:26-35` |
| E2E: progress bar fills over the duration, no double dwell (R-21) | Yes | `CarouselTests.The_progress_bar_fills_…`; `A_new_manifest_during_a_slide_takes_effect_…_without_lengthening_the_slide`; `SlideTypeTests` gap assertion |
| At most 3 layers, ADR-0008 release order | Yes | `__maxLayers` assertion; `carousel.js:243` releases before the next `prepare` |
| Image / WebPage / Html slides; Html sandbox `allow-scripts` only | Yes | `SlideTypeTests.Images_and_web_pages_play_…`; `slides/html.js:15` |
| Preloading + 3 s grace; skipping without a second dwell | Yes | `carousel.js:138-159,209-212`; PLAYER_SPEC §2 "Skipping" |
| Empty state (R-23), including "nothing can be shown" with a bounded retry | Yes | `CarouselTests.A_screen_without_content_…`; `SlideTypeTests.A_screen_whose_slides_all_fail_to_load_…` |
| Single slide: no transition, no progress | Yes, but its content never refreshes | `CarouselTests.A_single_slide_stays_…`; finding 1 |
| Offline cycling + cached start, also with `?token=` (R-24, R-41) | Yes | `CarouselTests.It_keeps_cycling_when_the_server_is_unreachable_…`, `A_kiosk_opened_with_its_screen_address_plays_its_cache_…`; `player.js:103-109` |
| Schedule re-check on the client, at show time (R-03) | Yes | `ClockTests.An_offline_screen_stops_showing_a_slide_when_its_schedule_ends` (now asserts the slide after it comes first); `carousel.js:147` |
| Admin preview (R-13), Editor+, 401/403/404, 404 message | Yes | `ManifestTests` preview tests; `CarouselTests.Previewing_a_screen_that_no_longer_exists_says_so` |
| Test hooks (`data-state`, `timeScale` only with `test=1`) | Yes | `config.js` |
| Demo seed (Development or `SIGNAGE__SEEDDEMO=true`, empty DB only, multi-replica safe) | Yes | `DemoSeedTests` incl. `Replicas_starting_together_…`; advisory lock in `DemoSeeder.cs:33-46` |
| Daily reload (postponed when unreachable; at a transition; one slide at the check; a handed-over reload is taken back) | Yes | `ClockTests` (4 tests incl. `A_reload_handed_to_a_carousel_that_then_stops_cycling_…`); `reload.js:39-60` |
| WP-07 NITs (ADR-0008 wording) | Yes | `docs/adr/0008-video-lifecycle.md` |
| WP-06 follow-ups, now tested | Yes | `PairingTests.A_claim_that_loses_a_race_does_not_cancel_…`, `A_devices_earlier_code_still_pairs_it_…` |
| WP-03 shared srcdoc helper | Yes | `HtmlSlideDocumentTests`, `ManifestTests.Html_slide_previews_…` |
| Security (sandbox, `textContent`, preview authz) | Yes | unchanged from round 1; `strings.js`/`player.js` messages use `textContent` |
| Docs truthful | Yes | PLAYER_SPEC §2 "Skipping", §9 cached boot with a token, §10 |

## Previous round follow-up
| Round-1 # | Status | Evidence |
|---|---|---|
| 1 MAJOR shadow during the dwell | Fixed | The shadow has moved to `.slide.animating` (`player.css:31-35`). The E2E test asserts `box-shadow: none` on the waiting layer. |
| 2 MAJOR double dwell on a manifest change / skip | Fixed | `advance()` + `adoptPending()` (`carousel.js:138-182`), with no second `dwell`. Two E2E tests use activation-gap assertions. |
| 3 MAJOR hot loop when nothing can be shown | Fixed | `showFirst()` makes one pass, then the empty state and a 10 s idle (`carousel.js:87-95`). E2E test with a bounded request count. |
| 4 MAJOR cached boot blocked by the token exchange | Fixed | `player.js:103-109`: the cache plays before `takeTokenFromUrl()`. E2E kiosk-restart test. |
| 5 MINOR reload stuck after the carousel stops cycling | Fixed | `reload.js:45-58`, and `isCycling` now needs a current slide. Clock test with a stand-in carousel. |
| 6 MINOR demo-seed race | Fixed | Advisory xact lock inside the execution strategy. Integration test with two factories. |
| 7 MINOR untested WP-06 follow-ups | Fixed | Two integration tests. `ClaimRaceInterceptor` makes the lost race deterministic. |
| 8 MINOR schedule checked at prepare time | Fixed | `isActive` re-check in `advance()`. The Clock test asserts the order. |
| 9 NIT weak ETag | Fixed | `EntityTagHeaderValue` weak compare, theory test. |
| 10 NIT preview 404 | Fixed | `onNotFound` + localised message, E2E test. |
| 11 NIT concurrent claims comment | Fixed | `PairingService.cs:191-192`. |

## Notes for the product owner
- The round-1 fixes are thorough and well tested. The one remaining item (finding 1) is that a screen with only one
  slide never shows edits to that slide until its daily reload. You would hit this quickly with a "menu of the
  day" style screen. It's a small, contained fix in the carousel's single-slide branch.
