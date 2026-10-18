# Review WP-10 — round 2
- Branch: wp-10-live-updates @ 911965f
- Verdict: APPROVE
- Verified:
  - `dotnet build --no-incremental`: 0 warnings, 0 errors.
  - `dotnet test`: 467 total, 410 passed, 0 failed, 57 skipped (E2E tests are skipped outside Docker, as designed).
  - `scripts/test-e2e.sh`: 57 total, 49 passed, 0 failed, 8 skipped (opt-in spikes only). Includes the new
    `An_edit_saved_while_the_screen_is_fetching_its_manifest_still_arrives_within_5_seconds` and
    `A_version_reload_that_could_not_happen_is_tried_again`.
  - `docker compose up -d --build`, then manual checks:
    - `/screen` carries `<meta name="signage-version" content="0.1.0+2f007e0b9aa1">` (the player build).
    - I gave the demo screen a known token in the DB and exchanged it at `/api/player/session` (204). A normal heartbeat
      gives 204. A 20 KB heartbeat body gives **413**, so the 16 KB `RequestSizeLimit` metadata is enforced by Kestrel.
    - Shutdown (round-1 finding 1): with an SSE client connected to `/api/player/events`, `docker compose stop -t 40 app`
      took **0.27 s** (round 1: 30 s). The curl client's stream ended.
    - `docker compose down -v` afterwards.
  - I checked in the vendored `htmx.min.js` that `HX-Redirect` is handled before the status-based swap logic, so the
    `401` + `HX-Redirect` answer does send the browser to the sign-in page.

## Findings
| # | Severity | Location | Finding | Expected |
|---|---|---|---|---|
| 1 | NIT | `src/Signage.Web/wwwroot/screen/reload.js:44-67`, `carousel.js:73-75` | The version reload and the daily reload share the one `beforeTransition` slot. If a version reload is armed with `runBeforeNextTransition` and the carousel then stops cycling before its next transition (the playlist drops to one slide, or the mode becomes Single video), the task never runs and `armed` stays `true`. The new version then waits for the daily reload (up to 26 h). This is the same kind of gap as round-1 finding 4a, but it needs two things to coincide, so it is unlikely. | When the retry timer fires, or at the daily check, take the task back if the carousel no longer cycles and reload directly. Or give the carousel a small list of before-transition tasks instead of one slot. |
| 2 | NIT | `src/Signage.Web/Features/Player/PlayerApi.cs:116` | The commit added a stray parenthesis: `// R-13): an editor or admin previews…`. | `// R-13: …` |
| 3 | NIT | `tests/Signage.E2E/Player/LiveUpdateTests.cs` (`A_version_reload_that_could_not_happen_is_tried_again`) | It waits for the refused `/screen` check with a `Task.Delay(100)` polling loop over a counter. That works, but a `TaskCompletionSource` set in the route handler would express the wait directly. | Signal from the route handler and await it with a timeout. |

## Requirement check
| R-ID / criterion | Met? | Evidence (test name or file:line) |
|---|---|---|
| R-25 push via SSE, ≤60 s poll fallback | Yes | E2E `An_edit_in_the_admin_reaches_the_screen_within_5_seconds`, `An_edit_saved_while_the_screen_is_fetching_its_manifest_still_arrives_within_5_seconds`; integration `A_change_reaches_the_screens_event_stream`, `A_screen_only_hears_about_its_own_changes_and_about_changes_to_all`, `Hundreds_of_affected_screens_fit_in_notifications`, `After_the_listener_loses_its_connection_players_fetch_and_later_events_still_arrive`; `connection.js` `again` flag and 60 s poll |
| R-26 self-update reload | Yes | E2E `A_screen_reloads_once_when_the_server_runs_a_new_version`, `A_version_reload_that_could_not_happen_is_tried_again`; integration `The_player_page_and_the_manifest_carry_the_same_player_build`, `The_player_build_changes_when_any_player_file_changes`; `PlayerBuild.cs` |
| R-33 status: online within 90 s, last seen, IP, version, UA | Yes | integration `The_screens_list_shows_online_until_90_seconds_without_a_heartbeat`, `A_heartbeat_records_status_without_touching_the_screens_version`, `The_status_refresh_neither_keeps_a_session_alive_nor_hides_a_signed_out_session`; E2E `The_screens_list_shows_a_playing_screen_as_online_with_its_player_details` |
| R-63 fan-out across replicas through LISTEN/NOTIFY | Yes | integration `Two_app_instances_sharing_one_database_both_deliver_an_event` |
| Forwarded-headers configuration | Yes | `ForwardedHeadersSetup.cs`; integration `Behind_a_trusted_proxy_the_forwarded_client_ip_is_recorded`, `Without_forwarded_headers_enabled_the_header_is_ignored`, `An_invalid_known_network_stops_start_up_with_a_clear_message` |
| Accept: edit → screen within 5 s (E2E) | Yes | see R-25 |
| Accept: two instances, one DB, both deliver (integration) | Yes | see R-63 |
| Accept: offline after 90 s (fake time) | Yes | see R-33 |
| Clean shutdown with connected players | Yes | integration `Stopping_the_app_ends_open_event_streams_at_once`; measured 0.27 s in compose |
| Security: authz on the new endpoints, input bounds | Yes | `/events` and `/heartbeat` `RequireAuthorization(ScreenPolicy)`; `The_event_stream_and_the_heartbeat_need_a_screen_token`; `A_heartbeat_with_more_problems_than_a_player_keeps_is_refused`; 413 above 16 KB (checked by hand) |
| Glossary / localisation / TimeProvider | Yes | no new UI strings in the fix commit; `Task.Delay(..., time, ct)`; no "monitor/card/feed" |
| Docs truthful | Yes | ARCHITECTURE player API table (build id, heartbeat limits), PLAYER_SPEC §9 (reload retry, build), ADR-0010 §4 and §7–9 |

## Previous round follow-up
| Round-1 # | Status | Evidence |
|---|---|---|
| 1 MAJOR: SSE held shutdown 30 s | Fixed | `PlayerEvents.cs:19-20` links `ApplicationStopping`; integration test `Stopping_the_app_ends_open_event_streams_at_once`; 0.27 s measured |
| 2 MAJOR: event during a fetch was lost | Fixed | `connection.js` `again` flag (`fetchNow` sets it while no `wake`; the loop refetches instead of pausing); E2E test holds a manifest response across a second edit and asserts the second edit shows within 5 s |
| 3 MINOR: status refresh renews the session / swallows sign-out | Fixed | `OnCheckSlidingExpiration` skips renewal for `X-Background-Refresh`; htmx requests get `401` + `HX-Redirect`; ADR-0010 §9; integration test checks both (no `Set-Cookie` after 7 h for the background refresh, `Set-Cookie` for a normal visit, `HX-Redirect` to `/admin/login`) |
| 4 MINOR: reload not re-armed; version ignores build | Fixed | (a) `reloadSoon` re-arms one minute after a failed check (E2E test with a refused `/screen` and a fake clock); see new NIT 1 for a remaining narrow case. (b) `PlayerBuild` = `<Version>+<hash of wwwroot/screen>`, used on `/screen` and as `appVersion`; documented |
| 5 MINOR: listener reconnect untested | Fixed | `After_the_listener_loses_its_connection_players_fetch_and_later_events_still_arrive` (`pg_terminate_backend` on the LISTEN session, fake-clock back-off, broadcast, later event) |
| 6 NIT: heartbeat never stopped | Fixed | `heartbeat.js` returns `stop()`; `player.js` calls it in `onUnauthorized` |
| 7 NIT: keep-alive timer per event | Fixed | `PlayerEvents.cs` reuses `keepAlive` until it fires |
| 8 NIT: sleep instead of wait | Fixed | `document.body.dataset.events === 'open'` hook |
| 9 NIT: unbounded heartbeat | Fixed | 20 problems max (400), 16 KB body (413) |
| 10 NIT: pg_notify failure fails a saved change | Fixed | `PostgresScreenNotifier` catches and logs `NpgsqlException`/`InvalidOperationException`/`TimeoutException`; ADR-0010 §7 |

## Notes for the product owner
- Players now reload whenever the player files change, even without a `<Version>` bump, so you no longer have to remember
  to bump the version for screens to pick up a fix.
- An open Screens tab no longer keeps an admin signed in. After 12 h without other activity, the tab goes to the sign-in
  page by itself on its next refresh.
- The notes from round 1 about `SIGNAGE__FORWARDEDHEADERS__KNOWNNETWORKS` behind a reverse proxy still apply.
