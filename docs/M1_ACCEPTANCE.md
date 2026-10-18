# Milestone 1 — acceptance

Every requirement of `PROJECT_BRIEF.md` with what proves it. Test names are the method names; the prefix says where
they live:

- **U** — `tests/Signage.UnitTests` (`dotnet test`)
- **I** — `tests/Signage.IntegrationTests`: the real app in-process against PostgreSQL in Testcontainers (`dotnet test`)
- **E** — `tests/Signage.E2E`: Playwright with Chromium inside Docker against the containerised app (`scripts/test-e2e.sh`)
- **M** — a manual check, described where it appears

Classes are given once per row; several tests in a row belong to the class named before them.

## Content

| R-ID | Requirement | Proof |
|---|---|---|
| R-01 | Slide types: Image, Video (MP4), Web page, HTML; images and videos are external URLs | U `SlideValidatorTests`: `A_complete_slide_of_each_type_is_valid`, `Url_types_require_a_url`, `Urls_must_be_absolute_http_or_https`. I `SlidePagesTests`: `An_editor_creates_an_image_slide_and_lands_on_its_edit_page`, `Each_slide_type_keeps_only_its_own_fields`. E `SlideTypeTests.Images_and_web_pages_play_and_a_broken_image_is_skipped`, `VideoTests.A_video_plays_only_once_fully_in_view_and_the_next_slide_comes_when_it_ends`, `CarouselTests.Slides_play_in_the_resolved_sequence_and_loop` (HTML). No upload exists anywhere (M: the slide form has URL fields only) |
| R-02 | Global default duration (10 s), per-slide override; videos play to the end | U `SlideDurationTests` (both), `SlideValidatorTests.Durations_must_be_between_1_and_3600_seconds`, `No_duration_means_the_global_default`, `Videos_ignore_the_duration_even_when_out_of_range`. I `SettingsPageTests.Admins_can_change_all_settings`, `ManifestTests.The_manifest_lists_enabled_slides_in_sequence_order_with_their_windows_but_not_expired_ones`. E `CarouselTests.The_progress_bar_fills_over_the_slide_duration_and_never_more_than_three_layers_exist`, `VideoTests.A_video_plays_only_once_fully_in_view_and_the_next_slide_comes_when_it_ends` |
| R-03 | Active from/until; skipped outside; entered in the system time zone, stored as UTC | U `SlideScheduleTests` (both), `ScheduleClockTests` (7), `SequenceResolverTests.Out_of_schedule_slides_are_skipped_in_every_section`, `Schedule_boundaries_are_inclusive_at_the_start_and_exclusive_at_the_end`. I `SlidePagesTests.Schedule_times_are_entered_in_the_system_time_zone_and_stored_as_utc`, `A_time_skipped_by_daylight_saving_is_rejected`, `ModelRulesTests.Schedule_times_round_trip_as_utc_instants`. E `ClockTests.An_offline_screen_stops_showing_a_slide_when_its_schedule_ends`, `A_slide_whose_schedule_ended_is_not_kept_on_screen_when_nothing_else_can_be_shown` |
| R-04 | Disable without deleting | U `SequenceResolverTests.Disabled_slides_are_skipped_in_every_section`. I `SlidePagesTests.Slides_can_be_switched_off_and_on_from_the_list`, `ManifestTests.The_manifest_lists_enabled_slides_in_sequence_order_with_their_windows_but_not_expired_ones` |
| R-05 | HTML slides sandboxed: `srcdoc`, `allow-scripts`, never `allow-same-origin` | I `SlidePagesTests.Titles_and_html_are_encoded_and_html_previews_never_get_same_origin`, `ManifestTests.Html_slide_previews_in_the_admin_use_the_shared_base_style`. U `HtmlSlideDocumentTests` (both). E `SlideLibraryTests.An_html_slide_created_in_the_browser_shows_up_in_the_library_with_a_sandboxed_preview`. M: `wwwroot/screen/slides/html.js` sets `sandbox="allow-scripts"` only |
| R-06 | Web-page slides with a note about framing; no proxy | E `SlideLibraryTests.A_web_page_slide_explains_that_some_sites_refuse_framing`, `SlideTypeTests.Images_and_web_pages_play_and_a_broken_image_is_skipped` |
| R-07 | Video muted by default, per-slide "play audio", docs on kiosk flags | U `SlideValidatorTests.Play_audio_only_sticks_on_videos`. E `SlideLibraryTests.A_video_slide_offers_play_audio_with_a_kiosk_hint_and_no_duration`, `VideoTests.A_video_plays_only_once_fully_in_view_and_the_next_slide_comes_when_it_ends` (muted), `A_video_with_sound_plays_with_sound_when_the_browser_allows_it_and_muted_when_it_does_not`, `A_video_with_sound_plays_muted_in_a_browser_that_refuses_autoplay_with_sound`. Docs: README "Kiosk browser flags"; ADR-0009 |
| R-08 | Playlists: CRUD, ordered slides, a slide in several playlists | I `PlaylistPagesTests` (16), e.g. `An_editor_creates_a_playlist_and_adds_slides_in_order_including_duplicates`, `Moving_an_entry_rewrites_positions_densely`, `Deleting_a_playlist_removes_its_assignments_and_keeps_the_other_assignments_dense`. U `SequenceResolverTests.The_same_slide_in_different_playlists_plays_in_each`. E `PlaylistEditorTests.Slides_are_added_reordered_with_buttons_and_by_dragging_and_the_order_persists` |

## Screens, groups, composition

| R-ID | Requirement | Proof |
|---|---|---|
| R-10 | Screens: unique name, location, mode Carousel/Single video | I `ScreenPagesTests.An_admin_creates_a_screen_and_names_are_unique_regardless_of_case`, `Single_video_mode_needs_a_valid_video_url_and_non_mp4_addresses_only_warn`, `ModelRulesTests.Names_are_unique_regardless_of_case` |
| R-11 | Groups: CRUD, many-to-many, ordered playlists, sort order | I `GroupPagesTests` (10), e.g. `Admins_create_groups_set_sort_order_and_members_and_both_old_and_new_members_are_notified`, `Editors_manage_a_groups_playlists_and_its_screens_are_notified`. U `SequenceResolverTests.Groups_are_ordered_by_sort_order_then_name` |
| R-12 | Composition: prepended → group playlists → screen playlists → appended; first occurrence of a playlist; filtered; loops | U `SequenceResolverTests` (22), incl. `A_full_composition_matches_the_R12_example`, `A_playlist_in_two_groups_plays_only_in_the_first_group`. I `SequenceFromDatabaseTests.A_screen_graph_loaded_from_the_database_resolves_per_R12`, `ScreenPagesTests.Editors_compose_prepended_slides_playlists_and_appended_slides_with_dense_positions`. E `ScreenCompositionTests.A_screen_composed_from_a_group_playlist_and_its_own_slides_shows_the_resolved_sequence`, `CarouselTests.Slides_play_in_the_resolved_sequence_and_loop` |
| R-13 | Resolved sequence with sources; Preview opens the player in the admin's browser | I `ScreenPagesTests.The_resolved_sequence_on_the_page_equals_the_resolvers_output`, `ManifestTests.Editors_and_admins_can_preview_a_screen_with_their_admin_cookie`, `Preview_without_an_admin_session_is_a_401_not_a_redirect`, `Previewing_an_unknown_screen_is_a_404`. U `SequenceResolverTests.Sources_describe_where_each_slide_came_from`. E `CarouselTests.An_admin_previews_the_screen_in_their_own_browser`, `Previewing_a_screen_that_no_longer_exists_says_so` |
| R-14 | Single video mode: seamless loop, no transitions or progress bar, mode change live | E `VideoTests.Single_video_mode_loops_without_a_progress_bar_and_mode_changes_apply_at_once`, `Single_video_mode_recovers_from_failures_and_leaves_nothing_behind`, `A_single_video_url_that_never_answers_counts_as_failed_after_8_seconds`, `IdentifyTests.Identification_shows_over_single_video_mode` |

## Player behaviour

| R-ID | Requirement | Proof |
|---|---|---|
| R-20 | New slide slides in from the right over a still slide, firm easing, left-edge shadow | E `CarouselTests.The_incoming_slide_slides_over_a_still_outgoing_slide_with_a_shadow_and_firm_easing` (every frame sampled: outgoing x stays 0, shadow only while animating, easing profile) |
| R-21 | Subtle progress bar; follows playback for video | E `CarouselTests.The_progress_bar_fills_over_the_slide_duration_and_never_more_than_three_layers_exist`, `A_single_slide_stays_without_transitions_or_progress_bar`, `VideoTests.A_video_plays_only_once_fully_in_view_and_the_next_slide_comes_when_it_ends` (bar follows the video) |
| R-22 | Video starts when fully in view; next slide on end; resource handling per the spike (ADR-0008) | E `VideoTests` (12): `A_video_plays_only_once_fully_in_view_and_the_next_slide_comes_when_it_ends` (`currentTime` 0 and paused in every transition frame), `A_video_that_cannot_load_is_skipped`, `A_video_that_stops_making_progress_is_skipped_after_8_seconds`, `A_video_next_to_a_slide_that_cannot_load_plays_again_without_hammering_the_broken_host`, `A_video_that_becomes_the_only_slide_while_it_plays_loops`, `A_lone_video_that_fails_is_replaced_by_a_fresh_one_and_loops_again`. The soak (below): at most 2 `<video>` and 3 layers over 30 min. ADR-0008 (spike data) |
| R-23 | Empty state: screen name and clock, no error | E `CarouselTests.A_screen_without_content_shows_its_name_and_a_clock_not_an_error`, `SlideTypeTests.A_screen_whose_slides_all_fail_to_load_shows_the_empty_state_and_retries_calmly`, `PairingFlowTests.A_device_shows_a_code_the_admin_claims_it_the_device_plays_and_a_revoke_sends_it_back` |
| R-24 | Offline: keeps cycling the last sequence, respects schedules, reconnects quietly | E `CarouselTests.It_keeps_cycling_when_the_server_is_unreachable_and_starts_offline_from_its_cache`, `A_kiosk_opened_with_its_screen_address_plays_its_cache_while_the_server_is_unreachable`, `ClockTests.An_offline_screen_stops_showing_a_slide_when_its_schedule_ends`, `The_daily_reload_waits_while_the_server_is_unreachable_and_happens_at_a_transition_once_it_answers`, `IdentifyTests.The_info_overlay_shows_a_dim_dot_while_the_screen_is_offline` (and back online) |
| R-25 | Live updates within ~5 s by SSE; polling ≤ 60 s fallback | E `LiveUpdateTests.An_edit_in_the_admin_reaches_the_screen_within_5_seconds` (real time, poll at 60 s), `An_edit_saved_while_the_screen_is_fetching_its_manifest_still_arrives_within_5_seconds`, `CarouselTests.A_new_manifest_during_a_slide_takes_effect_at_the_next_transition_without_lengthening_the_slide`. I `LiveUpdateTests.A_change_reaches_the_screens_event_stream`, `A_screen_only_hears_about_its_own_changes_and_about_changes_to_all`, `After_the_listener_loses_its_connection_players_fetch_and_later_events_still_arrive`, `ManifestTests.An_unchanged_manifest_answers_304_and_any_change_gives_a_new_etag`; `GroupPagesTests.Content_changes_notify_the_screens_that_show_the_content` |
| R-26 | Players reload when the server version changes | E `LiveUpdateTests.A_screen_reloads_once_when_the_server_runs_a_new_version`, `A_version_reload_that_could_not_happen_is_tried_again`, `A_version_reload_waiting_for_a_transition_happens_anyway_when_the_screen_stops_cycling`. I `LiveUpdateTests.The_player_page_and_the_manifest_carry_the_same_player_build`, `The_player_build_changes_when_any_player_file_changes`. ADR-0010 |

## Identification and status

| R-ID | Requirement | Proof |
|---|---|---|
| R-30 | Info overlay toggle: name and server-observed IP | E `IdentifyTests.The_info_overlay_appears_and_disappears_live_with_the_global_setting`. I `LiveUpdateTests.Behind_a_trusted_proxy_the_forwarded_client_ip_is_recorded`, `Without_forwarded_headers_enabled_the_header_is_ignored` |
| R-31 | Identify one: thick pulsing red border + name, 30 s | E `IdentifyTests.Identify_shows_a_pulsing_red_border_and_the_name_and_a_new_identify_restarts_the_timer` (border, 1 Hz pulse, fade, timer restart). I `LiveUpdateTests.Identify_reaches_only_that_screen_and_identify_all_reaches_every_screen_with_30_seconds`, `Identifying_needs_a_signed_in_user_and_an_existing_screen`, `ScreenPagesTests.Identify_says_so_when_the_screens_could_not_be_told` |
| R-32 | Identify all: every screen shows its name, 30 s | E `IdentifyTests.Identify_all_shows_every_screens_name_without_the_border`. I `LiveUpdateTests.Identify_reaches_only_that_screen_and_identify_all_reaches_every_screen_with_30_seconds` |
| R-33 | Screens list: online/offline (90 s), last seen, last IP, player version, user agent | I `LiveUpdateTests.The_screens_list_shows_online_until_90_seconds_without_a_heartbeat` (fake time), `A_heartbeat_records_status_without_touching_the_screens_version`, `The_status_refresh_neither_keeps_a_session_alive_nor_hides_a_signed_out_session`, `ScreenPagesTests.A_heartbeat_between_loading_and_saving_the_form_does_not_cause_a_conflict`. E `LiveUpdateTests.The_screens_list_shows_a_playing_screen_as_online_with_its_player_details` |

## Pairing

| R-ID | Requirement | Proof |
|---|---|---|
| R-40 | Pairing code: 6 characters, unambiguous, 15 min; admin enters it and names or picks a screen; token as HttpOnly cookie | U `PairingCodeTests` (4). I `PairingTests` (29), e.g. `A_device_gets_a_formatted_code_valid_for_15_minutes_and_only_hashes_are_stored`, `After_an_admin_claims_the_code_the_device_receives_the_token_once_as_an_http_only_cookie`, `Codes_expire_after_15_minutes_and_can_no_longer_be_claimed`, `A_code_can_be_claimed_only_once`, `Starting_pairing_is_rate_limited_per_client`, `A_devices_earlier_code_still_pairs_it_after_a_newer_code_was_shown`. E `PairingFlowTests.A_device_shows_a_code_the_admin_claims_it_the_device_plays_and_a_revoke_sends_it_back` |
| R-41 | Pre-configured URL with an embedded token | I `PairingTests.A_screen_address_is_shown_once_exchanged_for_the_cookie_and_replaces_earlier_pairing`, `An_unknown_token_cannot_be_exchanged`, `The_screen_address_exchange_limit_is_configurable`. E `PairingFlowTests.A_pre_configured_screen_address_pairs_the_device_and_disappears_from_the_address_bar`, `A_kiosk_that_boots_before_the_server_answers_keeps_its_token_and_pairs_later` |
| R-42 | Re-pair or revoke; revoke invalidates the token at once | I `PairingTests.Re_pairing_replaces_the_old_device`, `Revoking_ends_access_at_once_and_the_401_removes_the_cookie`, `A_revoke_before_the_device_collects_its_token_cancels_the_pairing`. E `PairingFlowTests.A_device_shows_a_code_the_admin_claims_it_the_device_plays_and_a_revoke_sends_it_back` |

## Accounts and security

| R-ID | Requirement | Proof |
|---|---|---|
| R-50 | Local accounts, ASP.NET Core password hasher, lockout | U `PasswordPolicyTests` (3). I `SignInTests` (12), e.g. `Repeated_failures_lock_the_account_until_the_lockout_expires`, `A_burst_of_parallel_guesses_gets_at_most_the_allowed_number_of_password_checks`, `AccountConcurrencyTests` (4), `SessionInvalidationTests` (6). E `AdminShellTests.A_wrong_password_shows_a_friendly_message_and_stays_on_the_page` |
| R-51 | First run: one-time "create first admin" page, or bootstrap env vars | I `SetupTests` (4), incl. `Concurrent_first_admin_requests_create_exactly_one_user`; `BootstrapTests` (3). M: README quick start verified on a clean clone (WP-12) |
| R-52 | Only admins manage users; new/reset users change their password first | I `UserAdminTests` (11), `RoleMatrixTests` (9), e.g. `Editors_cannot_create_users`, `A_new_user_signs_in_with_the_temporary_password_and_must_change_it_first`, `The_last_active_admin_cannot_be_demoted_or_deactivated` |
| R-53 | Antiforgery, SameSite=Lax + HttpOnly cookies, output encoding, no secrets, 256-bit hashed screen tokens, README says firewall it | I `SignInTests.Posting_without_an_antiforgery_token_is_rejected`, `The_auth_cookie_is_http_only_and_same_site_lax_and_secure_over_https`, `PlaylistPagesTests.Htmx_posts_without_the_antiforgery_header_are_rejected`, `UserAdminTests.User_names_are_html_encoded_in_the_list`, `SlidePagesTests.Titles_and_html_are_encoded_and_html_previews_never_get_same_origin`, `PairingTests.Requests_without_or_with_an_unknown_token_are_unauthorized`. U `ScreenTokensTests` (3). M: no secrets in git (the compose password is a labelled demo value); README "Security" |

## Operations

| R-ID | Requirement | Proof |
|---|---|---|
| R-60 | One multi-arch image, non-root, env config, `/healthz` and `/readyz`, migrations at start-up that can be switched off | I `HealthEndpointTests` (4), `DatabaseStartupTests` (7), incl. `With_migrations_switched_off_an_empty_database_fails_startup_with_a_clear_message`, `Every_migration_can_be_reverted_to_an_empty_schema_and_applied_again`. E `HealthSmokeTests.Healthz_loads_in_the_browser`. M: `docker buildx build --platform linux/amd64,linux/arm64 .` builds both (verified in the WP-12 review); the image runs as the chiseled image's non-root `app` user (`USER $APP_UID`); the manual-migration steps in the README were run in the WP-12 review |
| R-61 | `docker compose up` gives a working system at http://localhost:8080 | M: the README quick start on a clean clone, from `git clone` to a paired screen playing "Welcome (demo)" (WP-12, by the developer and again by the reviewer). The E2E suite itself runs the app from the same compose file |
| R-62 | Docs for Azure Container Apps + Azure PostgreSQL and for AWS + RDS | `docs/deploy/azure-container-apps.md`, `docs/deploy/aws.md` (reviewed in WP-12). Not run against real cloud accounts (see "For the product owner") |
| R-63 | Several replicas: events fan out through LISTEN/NOTIFY; data-protection keys in the database | I `LiveUpdateTests.Two_app_instances_sharing_one_database_both_deliver_an_event`, `DatabaseStartupTests.Two_replicas_starting_at_once_on_an_empty_database_both_succeed`, `Data_protection_keys_are_shared_through_the_database`, `DemoSeedTests.Replicas_starting_together_on_an_empty_database_seed_once_and_both_start`. ADR-0010 |
| R-64 | Admin strings from resource files; player strings localisable | U `ResourceCoverageTests` (6). M: `wwwroot/screen/strings.js` holds the player's strings by language |
| R-65 | MIT `LICENSE`, README (what, screenshots, quick start, security, no warranty), `CONTRIBUTING.md`, clean `.gitignore` | M: the files exist and were reviewed in WP-12; README screenshots are retaken by `scripts/update-readme-screenshots.sh` |

## Definition of done (PROJECT_BRIEF §8)

| # | Item | Status |
|---|---|---|
| 1 | Every work package `Done` with an approving review in `docs/reviews/` | WP-00 to WP-12 `Done` with APPROVE files; WP-13 approved in its own review (see `docs/reviews/WP-13-review-*.md`) |
| 2 | `dotnet test` passes | See "Verification run" |
| 3 | The E2E suite passes in Docker, covering pairing, transition and ordering, video lifecycle, single video mode, identify, live update and offline | See "Verification run"; the rows R-20 to R-42 above name the tests |
| 4 | Clean machine, `docker compose up`, README quick start → a working screen with the demo playlist | Verified on a clean clone in WP-12 (developer and reviewer) |
| 5 | A milestone-level review returns APPROVE | `docs/reviews/MILESTONE-M1-review-*.md` |
| 6 | This document | — |

## Soak (WP-13, ADR-0008)

`tests/Signage.E2E/Soak/PlayerSoak.cs` (opt-in: `SIGNAGE_E2E_SOAK=1 scripts/test-e2e.sh -class "Signage.E2E.Soak.PlayerSoak"`)
runs the shipped player for 30 minutes over three H.264 clips (3–4 s, real speed) alternating with three PNG images at
`timeScale=0.05`, in headless Chromium inside the Playwright container (software decoding). It samples the browser's
PSS from `/proc` every 100 transitions after a forced GC, with tracing off and without the CDP Media domain. It fails if
growth from the end of the first third to the end exceeds 2 × 0.15 MB per transition.

Passing run (2026-10-18), browser PSS in MB:

| Transitions | 0 | 100 | 200 | 300 | 400 | 500 | 600 | 700 | 800 | 900 |
|---|---|---|---|---|---|---|---|---|---|---|
| PSS | 352 | 504 | 590 | 597 | 618 | 660 | 666 | 689 | 726 | 735 |

- 900 transitions in 30.3 minutes. At most **2** slide layers and **1** `<video>` element at every sample.
- Growth from transition 300 to 900: **138 MB = 0.230 MB per transition**, against 180 MB allowed. **Pass.**
- An earlier full run (its only failure was the test's own layer check on the baseline sample, fixed since) measured
  733 MB at 900 transitions and 0.215 MB per transition: the figures repeat.
- **Note:** this is above the WP-07 spike's 0.15 MB per transition (within the 2× guard). Extrapolated linearly to
  10 s slides (8,640 transitions a day) it would be about **2 GB a day** rather than 1.3 GB. The daily reload still
  bounds it, but on a 2 GB device that is tight; `config.dailyReload` can be shortened (ADR-0008). Raised with the
  product owner below.

## Verification run

On branch `wp-13-acceptance` at `5fcdd58` (2026-10-18), x86_64 Linux host with Docker:

- `dotnet build`: 0 warnings, 0 errors.
- `dotnet test`: 478 tests, 0 failed (413 passed; the 65 E2E tests skip outside Docker by design).
- `scripts/test-e2e.sh`: 65 tests, 0 failed, 10 skipped: the opt-in WP-07 spike measurements (8), the README
  screenshot generator and the 30-minute soak, which ran separately (above).
- README quick start: verified on a clean clone in WP-12 by the developer and the reviewer.

## For the product owner

- **Cloud guides** (R-62) are written and reviewed but haven't been run against real Azure or AWS accounts.
- **Open question** (not blocking): kiosks that start from a pre-configured screen address may exchange it 10 times per
  client IP and minute (`SIGNAGE__SCREENADDRESSEXCHANGESPERMINUTE`). Many kiosks behind one NAT that boot together
  (after a power cut) then take a few minutes to all get through; they play their cached content meanwhile. Keep 10 as
  the default, or raise it (for example to 60)? The token is 256 bits, so the limit is about load, not guessing.
- **Daily reload** (ADR-0008): screens reload themselves once a day at night to bound browser memory, as you confirmed.
- **Memory rate:** the soak measured 0.23 MB per transition (the spike estimated 0.15). Within the regression guard,
  but at 10 s slides that extrapolates to about 2 GB a day before the nightly reload: tight for a 2 GB Raspberry Pi
  playing video all day. Options: accept it (most screens show longer slides), or make the reload more frequent by
  default (e.g. every 12 h) in `config.js`.
