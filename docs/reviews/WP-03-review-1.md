# Review WP-03 — round 1
- Branch: wp-03-slides @ ac65461
- Verdict: APPROVE
- Verified:
  - `git diff main...wp-03-slides --stat`: 32 files, +1874/−11; full diff read.
  - `dotnet build --no-incremental`: Build succeeded, 0 warnings, 0 errors.
  - `dotnet test`: 246 total, 239 passed, 0 failed, 7 skipped (the E2E tests, which only run in the container).
  - `scripts/test-e2e.sh`: exit 0, `Signage.E2E Total: 7, Errors: 0, Failed: 0, Skipped: 0`. I also checked the screenshots
    `artifacts/e2e/screens/slides-library.png` and `slides-edit-html.png`. The library list, the thumbnail, the edit page and the scaled HTML preview render correctly.
  - Checked by hand: the runtime image is `aspnet:10.0-noble-chiseled-extra` (Dockerfile:19), so IANA tzdata is present for `ScheduleClock.Resolve`.
    The only supported culture is `en` (Program.cs:42), so `asp-format="{0:yyyy-MM-ddTHH:mm}"` produces a valid `datetime-local` value. The cascade rules for
    `playlist_slides`/`screen_slides` are in `ContentConfigurations.cs`/`ScreenConfigurations.cs`. Slides sit under `AuthorizeFolder("/Admin", Policies.Editor)`,
    and `MustChangePasswordFilter` is applied globally (AdminUi.cs:19-25).

## Findings
| # | Severity | Location | Finding | Expected |
|---|---|---|---|---|
| 1 | MINOR | src/Signage.Web/Features/Slides/SlideService.cs:95-99; docs/ARCHITECTURE.md:94 | Deleting a slide uses `ExecuteDelete` and the DB cascade. That leaves gaps in `playlist_slides.position` and `screen_slides.position` for every playlist or screen that used the slide, but ARCHITECTURE says "Positions are dense integers". So the documented invariant no longer holds after a delete, and WP-04 (move up/down) could reasonably rely on it. | Do one of two things: renumber the affected positions in the same transaction as the delete, or change ARCHITECTURE to say "ordered; gaps may appear after deletes, reorder rewrites densely" and add a note to WP-04 that it must tolerate gaps. Either way, add a test. A follow-up line on WP-04 is acceptable. |
| 2 | MINOR | src/Signage.Web/Resources/SharedResource.resx:277 (`Slides.Hint.PlayAudio`) | The help text says "see the README", but the README says nothing about autoplay or kiosk flags yet. R-07 needs that documentation. | Add a short README paragraph on the autoplay-with-sound kiosk flags now, or add an explicit follow-up to the README package in BACKLOG so the hint doesn't stay untrue. |
| 3 | MINOR | tests/Signage.E2E/Admin/SlideLibraryTests.cs:26-27, 31-33 | `WaitForURLAsync("**/admin/slides/*")` already matches the create page URL (`/admin/slides/create?type=Html`, because `*` matches `create?type=Html`), so it doesn't wait for the POST and redirect. The assertions that follow use `IsVisibleAsync()`, which doesn't retry. The same applies after clicking "Back to the slide library". The test passes because the click waits for the navigation to commit, but it is timing-dependent. | Use auto-retrying assertions (`Assertions.Expect(locator).ToBeVisibleAsync()`), or wait for a URL that only the edit page matches (for example a regex on a GUID) and for the library URL after the back click. |
| 4 | MINOR | src/Signage.Web/Pages/Admin/Slides/_SlidePreview.cshtml:16; docs/adr/0005-slide-library-details.md:67-71 | The ADR says previews are "the same rendering as the player, shrunk". PLAYER_SPEC:75 says the player wraps Html in a base stylesheet that resets margins and sets a black background, but the admin preview passes the raw `srcdoc`. The preview keeps the default 8 px body margin, and plain markup such as `<h1>Hello</h1>` shows black on black: the `iframe` element is black, the text is black by default, and the hint says "background is black unless you set one". | Give the base stylesheet a single source in code (WP-08 will need it anyway) and use it in both the preview and the player. Consider adding `color:#fff` to it so unstyled HTML is readable. A WP-08 follow-up is acceptable. |
| 5 | NIT | SharedResource.resx:357 (`Slides.Usage`) | "1 playlists · 1 screens": the plural is wrong for 1. | Use singular and plural keys, or a neutral wording such as "Playlists: 1 · Screens: 1". |
| 6 | NIT | src/Signage.Web/Pages/Admin/Slides/Index.cshtml:35 | The slide list's column header reuses `Users.Field.Status`. | Use a `Slides.Column.Status` key so the two can't drift apart. |
| 7 | NIT | src/Signage.Web/wwwroot/admin/admin.css:158 (`.split`) | At 1920 px the preview pane goes about 20 px past the content column (see `slides-edit-html.png`: the preview's right edge is at about 1506 px, the header's at about 1486 px). | Let the second column shrink, or size the preview to its container. |
| 8 | NIT | src/Signage.Web/Pages/Admin/Slides/SlideFormPage.cs:38-41 | `TempData["Warning"]` is set before `UpdateAsync`. If the save then fails with a concurrency conflict, the warning leaks into the next page view. | Set the warning only after the save succeeds. |
| 9 | NIT | src/Signage.Web/Pages/Admin/Slides/_SlidePreview.cshtml:10 | `@(Model.Url)#t=0.1` gives a second fragment when the URL already has one, so the poster frame hint is lost. | Strip any existing fragment before appending `#t=0.1`, or ignore this. |

## Requirement check
| R-ID / criterion | Met? | Evidence (test name or file:line) |
|---|---|---|
| R-01 four slide types, URL-only media | Yes | Create per type (`Create.cshtml.cs:13-48`); `SlidePagesTests.Each_slide_type_keeps_only_its_own_fields`; `SlideValidatorTests.A_complete_slide_of_each_type_is_valid`; unknown type returns 404 (`An_unknown_type_is_a_404`) |
| Validation: absolute http/https URLs, required fields per type | Yes | `SlideValidator.cs:46-75, 126-129`; `Urls_must_be_absolute_http_or_https` (javascript:, data:, file:, ftp:, relative URLs, `https://`); `Invalid_input_is_shown_next_to_the_field_and_nothing_is_saved`; `Html_slides_need_html`; `Very_large_html_is_rejected` |
| R-02 duration override; videos ignore duration | Yes | `SlideValidator.cs:77-82`; `Durations_must_be_between_1_and_3600_seconds`; `Videos_ignore_the_duration_even_when_out_of_range`; `No_duration_means_the_global_default`; the list shows the default (`Index.cshtml:50-62`) |
| R-03 schedule entered in the system TZ, stored as UTC | Yes | `ScheduleClock.cs`; `SlidePagesTests.Schedule_times_are_entered_in_the_system_time_zone_and_stored_as_utc` (round trip through form, DB, edit form and list); DST gap and ambiguous times in `ScheduleClockTests`, `A_time_skipped_by_daylight_saving_is_rejected`; `Until_before_from_is_rejected`; ADR-0005 documents the DST policy |
| R-04 enable/disable without deleting | Yes | `SlideService.SetEnabledAsync` (sets `UpdatedAt` explicitly); `Slides_can_be_switched_off_and_on_from_the_list` |
| R-05 HTML sandbox, never `allow-same-origin` | Yes | `_SlidePreview.cshtml:16`; `Titles_and_html_are_encoded_and_html_previews_never_get_same_origin` (list and edit); E2E checks `sandbox == "allow-scripts"` |
| R-06 WebPage framing note | Yes | `_SlideForm.cshtml:23-26`; E2E `A_web_page_slide_explains_that_some_sites_refuse_framing` |
| R-07 play-audio flag with help text | Partly | Flag only on videos (`Play_audio_only_sticks_on_videos`; E2E `A_video_slide_offers_play_audio_with_a_kiosk_hint_and_no_duration`). The README part is still missing (finding 2) |
| Thumbnail/preview in list: image, video `#t=0.1`, scaled iframe | Yes | `_SlidePreview.cshtml`; `admin.css` `.slide-preview`; screenshot |
| Search filter | Yes | `SlideService.ListAsync` (ILIKE with escaping); `Search_matches_titles_case_insensitively_and_treats_wildcards_literally` |
| Editor and Admin both allowed | Yes | `RoleMatrixTests` (4 slide routes, `true`), plus the anonymous redirect theory; all `SlidePagesTests` POSTs run as Editor |
| Optimistic concurrency on edit | Yes | `SlideService.UpdateAsync` (xmin original value); `A_stale_edit_form_reports_a_conflict` |
| Delete shows usage and removes links | Yes | `Deleting_a_slide_in_use_shows_where_and_removes_the_links` (but see finding 1 on position gaps) |
| WP-01 follow-up: ARCHITECTURE note on bulk updates and `UpdatedAt` | Yes | ARCHITECTURE.md:101-103 |
| WP-01 follow-up: test that updates overwrite `UpdatedAt` while inserts keep an explicit one | Yes | `TimestampTests.Updates_always_overwrite_UpdatedAt_while_inserts_keep_an_explicit_one` |
| Glossary / scope | Yes | No "card", "monitor" or "feed" in the diff; nothing out of scope |
| Localization (all UI strings through localizers) | Yes | All new strings are in `SharedResource.resx`; `Slide_type_texts_exist_for_every_type`; error keys covered by the resource-coverage test |
| Build warning-free, tests green | Yes | See Verified |

## Notes for the product owner
- **WebPage slides and same-origin.** PLAYER_SPEC says `allow-same-origin` is safe for WebPage slides "because the site is a different origin from the player". The validator doesn't enforce this: an editor can enter the Signage host's own URL. Today nothing that an editor controls is served from that origin, so there is no exploit. If a future feature serves editor-controlled HTML at a URL on the Signage host (for example a full-page slide preview route), consider rejecting WebPage URLs that point at the Signage host itself.
- **Previews load external content.** The library list loads every slide's external image, video or web page into the admin's browser, and those pages run their own scripts. This is what the backlog asked for. With a large library it can get heavy, so pagination may be worth an IDEAS entry later.
- **Time-zone changes move wall-clock times.** Schedules are stored in UTC. If the system time zone changes later, the wall-clock times shown in the admin change while the instants stay the same. That matches R-03's literal wording, but you may want the settings page to say so.
