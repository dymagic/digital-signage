# Review MILESTONE — round 1
- Branch: main @ 0ab5b85
- Verdict: APPROVE
- Verified:
  - `dotnet build`: Build succeeded, 0 warnings, 0 errors.
  - `dotnet test`: total 478, failed 0, succeeded 413, skipped 65 (the E2E project skips outside Docker by design). Integration tests ran against Testcontainers PostgreSQL (1 m 37 s).
  - `scripts/test-e2e.sh`: Signage.E2E total 65, errors 0, failed 0, skipped 10 (8 opt-in WP-07 spike measurements, the README screenshot generator and the 30-minute soak), exit 0, 143 s.
  - README quick start, followed literally on a fresh `git clone` into an empty directory with no leftover `signage` volumes or containers: `docker compose up -d --build` exit 0; `/` → 302 `/admin` → `/admin/setup`; created the first admin through the form (antiforgery token, 302 to `/admin`); "Demo screen" and "Welcome (demo)" present; `POST /api/player/pairing` gave a code; claimed it on **Screens → Pair screen → An existing screen → Demo screen** (lower-case code accepted); the device's next poll returned `paired` with `Set-Cookie: signage.screen=…; path=/api/player; samesite=lax; httponly`, a second poll returned 410; `GET /api/player/manifest` returned the demo sequence (Html, NASA image, …); `/api/player/events` answered `text/event-stream`; heartbeat 204; the screens list shows the screen Online. The container runs as uid 1654 (non-root). `docker compose down -v` afterwards.
  - Checked that all 117 test names cited in `docs/M1_ACCEPTANCE.md` exist in `tests/`, and that the per-class counts it quotes (PairingTests 29, SequenceResolverTests 22, VideoTests 12, PlaylistPagesTests 16, GroupPagesTests 10, SignInTests 12, UserAdminTests 11, RoleMatrixTests 9, ScheduleClockTests 7, DatabaseStartupTests 7, ResourceCoverageTests 6) match the code.
  - Not re-run: the 30-minute soak and the multi-arch `buildx` build (both recorded in WP-12/WP-13 and their reviews); the cloud guides (documented as not run against real accounts).

Scope of this review: the whole repository against PROJECT_BRIEF §4 and §8, ARCHITECTURE, PLAYER_SPEC and the ADRs. Read in full: `Program.cs`, `AdminUi.cs`, the user/auth wiring (`UsersServiceCollectionExtensions`, `UserPrincipal`, `MustChangePasswordFilter`, `UserAccounts`), `Login`/`Setup`, the screen and group detail page models and the screens list, every player API file (`PlayerApi`, `ScreenAuthentication`, `PlayerShell`, `PlayerEvents`, `HeartbeatService`, `ManifestService`, `PlayerBuild`), pairing (`PairingService`, `ScreenAccessService`, `ScreenTokens`, `PairingCode`), events (`PostgresScreenNotifier`, `ScreenEventListener`, `ScreenEventHub`, `ScreenEvent`, `AffectedScreens`), `SequenceResolver`, `SlideSchedule`, `ScreenGraphQueries`, the Slide/Playlist/Screen/Group/Settings services, `DatabaseInitializer`, data and forwarded-header setup, every player module under `wwwroot/screen/`, the admin layout and `admin.js`, the Dockerfile, both compose files, `.gitignore`/`.dockerignore`, README, ARCHITECTURE, PLAYER_SPEC, BACKLOG and M1_ACCEPTANCE. Sampled tests: RoleMatrixTests, the R-20 transition, progress bar and offline E2E tests, and the handler-level editor/admin tests.

## Findings
| # | Severity | Location | Finding | Expected |
|---|---|---|---|---|
| 1 | MINOR | `README.md:14`, `docs/M1_ACCEPTANCE.md:44` (R-24 row) | The README says "Screens keep playing from their cache when the network or the server is down". That holds for a **running** player. A screen that powers on or restarts its browser while the server is unreachable can't load `/screen` at all: there is no service worker, and the shell, JS and CSS come from the server. The browser shows its own error page. The E2E test `It_keeps_cycling_when_the_server_is_unreachable_and_starts_offline_from_its_cache` reloads with only `**/api/player/**` aborted, so `/screen` and its modules still load. It proves "boots from the cache while the API is unreachable", not a cold boot with the server down. That is what PLAYER_SPEC §9 asks for, so behaviour is to spec, but the README sentence promises more. | Make the README precise (e.g. "…keep playing from their cache while the server is down; a screen that restarts needs the server to load the player once"), and say the same in the R-24 row of M1_ACCEPTANCE. A service worker for a true offline cold start can go to IDEAS/BACKLOG as a later option. |
| 2 | NIT | `src/Signage.Web/Data/DataServiceCollectionExtensions.cs:22-24` | Data-protection keys go to the database unencrypted. On first start the app logs `warn: …XmlKeyManager[35]` ("No XML encryptor configured…"); I saw it in the quick-start run. Anyone with read access to the database can forge admin cookies. That is acceptable for the stated trust model, but nothing in the README or the ADRs mentions it, so an operator who sees the warning has no explanation. | One sentence in the README security section (or ADR-0004): the keys are stored unencrypted in the database, so protect database access like an admin password, and the start-up warning is expected. |
| 3 | NIT | `src/Signage.Web/Pages/Admin/Screens/Pair.cshtml:12` | Placeholder `ABC-123` uses `B`, `1` and `2`, which are deliberately not in the pairing alphabet (`PairingCode.Alphabet`). | Use a valid example such as `K7P-QMR`. The `K7P-Q2M` example in PLAYER_SPEC §8 and `PairingCode.cs` has the same issue (`2`). |
| 4 | NIT | `docs/ARCHITECTURE.md` §2 (solution layout) | Lists a top-level `deploy/` folder ("compose overrides") that doesn't exist. Cloud docs live in `docs/deploy/`. | Remove the line or point it at `docs/deploy/`. |
| 5 | NIT | `src/Signage.Web/Program.cs:66-75` | Static files also serve `wwwroot/screen/index.html` at `/screen/index.html`, without the version stamp (`PlayerShell`). A kiosk pointed at that URL plays but never self-updates (R-26), because `loadedVersion` is empty. | Exclude `screen/index.html` from static files or redirect it to `/screen`. Alternatively, note in the README that the address is `/screen`. |
| 6 | NIT | `src/Signage.Web/wwwroot/screen/slides/webpage.js:8`, `Pages/Admin/Slides/_SlidePreview.cshtml:14` | Web-page slides get `allow-scripts allow-same-origin` on the grounds that "the page is another origin". Nothing checks that. An editor can enter a URL on the app's own origin, and that frame then runs same-origin with the player and admin. I found no way to get attacker-controlled script onto the origin (no uploads; HTML slides are `srcdoc` without same-origin; Razor encodes output), so this is not exploitable today. | Defence in depth: have `SlideValidator` warn about (or reject) web-page URLs whose origin equals the request's origin, or drop `allow-same-origin` for such URLs. |

No BLOCKER or MAJOR findings.

Things I checked and found correct (no finding):
- **Authz:** the folder conventions plus `RequireAdmin()` in every admin-only handler (screen details/groups/revoke/address, group details/members) match the §3 matrix. Editors can identify, compose and preview. Handler-level tests exist (`Editors_see_the_screen_but_cannot_change_details_or_groups`, `Editors_cannot_pair_revoke_or_create_screen_addresses`, `Editors_cannot_create_rename_reorder_or_change_members_of_groups`, `Admins_delete_screens_and_editors_cannot`). The `Screen` policy accepts only the screen-token scheme, and the preview manifest uses the admin cookie and refuses must-change-password users.
- **Cookies and tokens:** the admin cookie is re-validated against the user row on every request (stamp, active, role). The screen token is 256-bit, stored as SHA-256 (unique filtered index), delivered once, revocable at once, and the 401 deletes the cookie. Revoke and delivery are serialised correctly by row locks (`CancelPendingClaimsAsync` vs. `DeliverAsync`). Open redirect is prevented (`Url.IsLocalUrl`). Antiforgery is on forms and htmx (`hx-headers`), and `GetAndStoreTokens` in the layout also sends `X-Frame-Options: SAMEORIGIN` on every admin page. The HTML-slide sandbox is `allow-scripts` only, in both the player and the admin preview.
- **Concurrency:** lockout claims are atomic (bounded password checks under parallel guesses). The first-admin and last-admin checks run under an advisory lock. Start-up is serialised by an advisory lock. Screen versions are app-managed, so heartbeats don't conflict with edits.
- **Multi-replica:** events go through LISTEN/NOTIFY with reconnect plus an "all manifests changed" catch-up. Data-protection keys are in the database. The only in-memory state is the SSE subscriptions and the per-replica rate limiters (documented in ADR-0007).
- **Player:** values match PLAYER_SPEC (`config.js`). Animation is transform-only. The covered slide is released before the next one is prepared (≤ 3 layers, ≤ 2 `<video>`). Videos release with pause → remove src → load. Watchdogs and caps are cleared on release. Single-video layers are all tracked and released. The daily reload and the version reload only happen after a 2xx from `/screen`.
- **Data:** migrations are reversible (tested by `Every_migration_can_be_reverted_to_an_empty_schema_and_applied_again`). Lookups are indexed (code, token hash, names, positions). Cascades are as documented. Positions are renumbered on cascading deletes.
- **Hygiene:** no secrets beyond labelled demo/test values, no files > 1 MB, no glossary violations ("monitor", "card" or "feed") in code, UI or tests.

## Requirement check
| R-ID / criterion | Met? | Evidence (test name or file:line) |
|---|---|---|
| R-01 Slide types | Yes | `SlideValidatorTests.A_complete_slide_of_each_type_is_valid`; `SlideTypeTests.Images_and_web_pages_play_and_a_broken_image_is_skipped`; `wwwroot/screen/slides/index.js` |
| R-02 Duration | Yes | `SlideDurationTests`; `ManifestService.cs` (`SlideDuration.Resolve`); `CarouselTests.The_progress_bar_fills_over_the_slide_duration…` |
| R-03 Scheduling (system TZ → UTC, half-open) | Yes | `SlidePagesTests.Schedule_times_are_entered_in_the_system_time_zone_and_stored_as_utc`, `A_time_skipped_by_daylight_saving_is_rejected`; `SequenceResolverTests.Schedule_boundaries_are_inclusive_at_the_start_and_exclusive_at_the_end`; `ClockTests.An_offline_screen_stops_showing_a_slide_when_its_schedule_ends` |
| R-04 Enable/disable | Yes | `SlidePagesTests.Slides_can_be_switched_off_and_on_from_the_list`; `SequenceResolverTests.Disabled_slides_are_skipped_in_every_section` |
| R-05 HTML sandbox | Yes | `slides/html.js:15` (`allow-scripts` only); `_SlidePreview.cshtml:17`; `SlidePagesTests.Titles_and_html_are_encoded_and_html_previews_never_get_same_origin` |
| R-06 Web-page note, no proxy | Yes | `SlideLibraryTests.A_web_page_slide_explains_that_some_sites_refuse_framing` (see NIT 6) |
| R-07 Video audio | Yes | `VideoTests.A_video_with_sound_plays_with_sound_when_the_browser_allows_it_and_muted_when_it_does_not`; README "Kiosk browser flags" |
| R-08 Playlists | Yes | `PlaylistPagesTests` (16); `PlaylistEditorTests.Slides_are_added_reordered_with_buttons_and_by_dragging_and_the_order_persists` |
| R-10 Screens | Yes | `ScreenPagesTests.An_admin_creates_a_screen_and_names_are_unique_regardless_of_case` |
| R-11 Groups | Yes | `GroupPagesTests` (10); `SequenceResolverTests.Groups_are_ordered_by_sort_order_then_name` |
| R-12 Composition | Yes | `SequenceResolverTests.A_full_composition_matches_the_R12_example`, `A_playlist_in_two_groups_plays_only_in_the_first_group`; `SequenceFromDatabaseTests` |
| R-13 Sequence preview | Yes | `ScreenPagesTests.The_resolved_sequence_on_the_page_equals_the_resolvers_output`; `CarouselTests.An_admin_previews_the_screen_in_their_own_browser` |
| R-14 Single video mode | Yes | `VideoTests.Single_video_mode_loops_without_a_progress_bar_and_mode_changes_apply_at_once` |
| R-20 Transition | Yes | `CarouselTests.The_incoming_slide_slides_over_a_still_outgoing_slide_with_a_shadow_and_firm_easing` (every frame sampled) |
| R-21 Progress bar | Yes | `CarouselTests.The_progress_bar_fills_over_the_slide_duration_and_never_more_than_three_layers_exist`; `carousel.js:371-385` (video) |
| R-22 Video lifecycle | Yes | `VideoTests` (12); ADR-0008; `carousel.js:359` (release before prepare) |
| R-23 Empty state | Yes | `CarouselTests.A_screen_without_content_shows_its_name_and_a_clock_not_an_error` |
| R-24 Offline resilience | Yes (running player; see MINOR 1 for cold boot) | `CarouselTests.It_keeps_cycling_when_the_server_is_unreachable_and_starts_offline_from_its_cache`; `ClockTests.The_daily_reload_waits_while_the_server_is_unreachable…` |
| R-25 Live updates | Yes | `LiveUpdateTests.An_edit_in_the_admin_reaches_the_screen_within_5_seconds` (E2E, poll at 60 s); integration `LiveUpdateTests.After_the_listener_loses_its_connection…` |
| R-26 Self-update | Yes | `LiveUpdateTests.A_screen_reloads_once_when_the_server_runs_a_new_version`; `PlayerBuild.cs` (see NIT 5) |
| R-30 Info overlay | Yes | `IdentifyTests.The_info_overlay_appears_and_disappears_live_with_the_global_setting`; `LiveUpdateTests.Behind_a_trusted_proxy_the_forwarded_client_ip_is_recorded` |
| R-31 Identify one | Yes | `IdentifyTests.Identify_shows_a_pulsing_red_border_and_the_name_and_a_new_identify_restarts_the_timer` |
| R-32 Identify all | Yes | `IdentifyTests.Identify_all_shows_every_screens_name_without_the_border` |
| R-33 Status | Yes | `LiveUpdateTests.The_screens_list_shows_online_until_90_seconds_without_a_heartbeat` (fake time); E2E `The_screens_list_shows_a_playing_screen_as_online_with_its_player_details`; verified by hand in the quick start |
| R-40 Pairing code | Yes | `PairingTests` (29); `PairingFlowTests.A_device_shows_a_code_the_admin_claims_it…`; verified by hand |
| R-41 Pre-configured URL | Yes | `PairingFlowTests.A_pre_configured_screen_address_pairs_the_device_and_disappears_from_the_address_bar` |
| R-42 Revoke | Yes | `PairingTests.Revoking_ends_access_at_once_and_the_401_removes_the_cookie` |
| R-50 Accounts, lockout | Yes | `SignInTests.Repeated_failures_lock_the_account_until_the_lockout_expires`, `A_burst_of_parallel_guesses_gets_at_most_the_allowed_number_of_password_checks` |
| R-51 First run | Yes | `SetupTests` (incl. concurrency), `BootstrapTests`; verified by hand in the quick start |
| R-52 User admin | Yes | `UserAdminTests` (11), `RoleMatrixTests` (9) |
| R-53 Web security baseline | Yes | `SignInTests.Posting_without_an_antiforgery_token_is_rejected`, `The_auth_cookie_is_http_only_and_same_site_lax_and_secure_over_https`; `ScreenTokensTests`; README "Security" |
| R-60 Container | Yes | `HealthEndpointTests`, `DatabaseStartupTests`; Dockerfile (chiseled, `USER $APP_UID`, uid 1654 observed) |
| R-61 docker-compose | Yes | Quick start run end-to-end in this review |
| R-62 Cloud docs | Yes (docs only, not run in a cloud) | `docs/deploy/azure-container-apps.md`, `docs/deploy/aws.md` |
| R-63 Multiple replicas | Yes | `LiveUpdateTests.Two_app_instances_sharing_one_database_both_deliver_an_event`; `DatabaseStartupTests.Data_protection_keys_are_shared_through_the_database` |
| R-64 i18n-ready | Yes | `ResourceCoverageTests` (6); `wwwroot/screen/strings.js` |
| R-65 OSS hygiene | Yes | `LICENSE` (MIT), README (what, screenshots, quick start, security, no warranty), `CONTRIBUTING.md`, `.gitignore` |
| §8.1 every package Done with APPROVE | Yes | `docs/BACKLOG.md` WP-00 to WP-13 `Done`; the last review file of each package in `docs/reviews/` is APPROVE |
| §8.2 `dotnet test` passes | Yes | 478 total, 0 failed (this review) |
| §8.3 E2E passes in Docker, covering the listed areas | Yes | 65 total, 0 failed, 10 opt-in skips (this review) |
| §8.4 clean-machine quick start reaches a playing demo screen | Yes | Fresh clone and empty volumes, followed literally (this review). Browser rendering of the demo is covered by the E2E pairing flow |
| §8.5 milestone review APPROVE | Yes | This file |
| §8.6 `M1_ACCEPTANCE.md` lists every R-ID with proof | Yes | All 36 R-IDs present; all 117 cited test names exist |

## Notes for the product owner
- **Cold start without the server (MINOR 1):** a screen that is switched on while the server is down shows the browser's own error page until it is reloaded. A screen that is already running keeps playing. If your TVs lose power together with the server (same circuit, same UPS), or the network often comes up after the TVs, consider a service worker later (offline cold boot), or use a kiosk browser that retries on network errors.
- **First-run window:** until someone creates the first admin, anyone who can reach port 8080 can do it. Set it up right after `docker compose up`, or use `SIGNAGE__BOOTSTRAP__ADMINEMAIL`/`…ADMINPASSWORD`. The README already says not to expose the app.
- **Open questions carried over from M1_ACCEPTANCE:** the screen-address exchange limit (10/min per IP behind one NAT), and the measured 0.23 MB per transition with the nightly reload (tight on a 2 GB Pi showing short slides all day). Both are your call. Neither blocks M1.
- **Before publishing:** the git history is authored with your own name and e-mail address on every commit, and the repo includes the `.claude/` agent and skill files and `docs/reviews/`, `docs/PROGRESS.md`, `docs/KICKOFF.md`. Neither is a secret, but decide whether you want them public.
