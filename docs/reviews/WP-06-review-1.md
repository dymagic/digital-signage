# Review WP-06 — round 1
- Branch: wp-06-pairing @ ed5dd7b
- Verdict: CHANGES_REQUIRED
- Verified:
  - `dotnet build`: Build succeeded, 0 warnings, 0 errors.
  - `dotnet test`: Passed, total 372, failed 0. The E2E project's tests are skipped outside Docker, as designed.
  - `scripts/test-e2e.sh`: exit 0, `Signage.E2E Total: 12, Errors: 0, Failed: 0, Skipped: 0`. Both new
    `PairingFlowTests` ran and passed.
  - Read the full `git diff main...wp-06-pairing`, ADR-0007, and the WP-06 entry and R-40/41/42/53 in the docs.

## Findings
| # | Severity | Location | Finding | Expected |
|---|---|---|---|---|
| 1 | MAJOR | `Features/Pairing/ScreenAccessService.cs:31-42`, `Features/Pairing/PairingService.cs:77-86,190-218` | **Revoke can be bypassed by a pending claim.** `RevokeAsync` only clears `screens.token_hash`. A pairing request that was claimed for that screen but not yet delivered stays deliverable. `PollAsync` delivers whenever `ClaimedScreenId` is set and never checks `ExpiresAt`. Cleanup only runs when someone starts a new pairing, so in practice the window has no limit. Example: the admin claims a code, the device is offline or was the wrong one, the admin clicks Revoke, and the device later polls and gets a fresh valid token anyway. That breaks R-42's "revoking invalidates … at once". `CreateAddressTokenAsync` has the same problem: a stale claim later overwrites the kiosk address token. | Revoke and "Create screen address" cancel undelivered claims for the screen, for example by setting `TokenDeliveredAt`/clearing the claim in the same statement batch. Delivery should also refuse claims older than a bounded window (e.g. `ExpiresAt`). Add integration tests: claim → revoke → poll returns 410 and the screen stays unpaired; claim → create address → poll doesn't replace the address token. |
| 2 | MAJOR | `wwwroot/screen/player.js:148-157,176-177`, `wwwroot/screen/api.js:43-50` | **A pre-configured kiosk that boots before the server is reachable dies silently (R-41).** `takeTokenFromUrl()` runs outside any `try`. If `fetch` rejects on a network error, `run()` rejects, and the player stays on an empty black `data-mode="boot"` page until someone reloads it. The token has already been removed with `replaceState`, so a reload or a later `appVersion` self-reload (R-26) can't retry it. A 429 or 5xx from `/session` also drops the token silently, and the device falls back to a pairing code. Kiosks often power up together with the network or server after an outage, so this is the main R-41 use case. | Keep the token in memory and retry the exchange with back-off on network errors, 429 and 5xx. Give up only on 401. Strip the address bar either right away (keeping the token in memory) or after success, but never lose the token on a transient failure. Add an E2E or JS-level test: route `/api/player/session` to abort once, then succeed, and check the device ends up in `player` mode. |
| 3 | MINOR | `wwwroot/screen/pairing.js:120-121` | The refresh deadline compares the server's `expiresAt` with the device clock (`Date.now()`). If the device clock runs ≥ 14 min fast (possible on closed networks without NTP), the inner loop never runs. The outer loop then calls `startPairing` back to back with no delay, burns the 10/min budget, flashes codes that are never polled, and the device can't pair. | Work from relative time. Return `expiresInSeconds` (or use the response `Date` header) and schedule the refresh against `performance.now()`. Always wait at least the poll interval between starts. |
| 4 | MINOR | `tests/Signage.IntegrationTests/Player/PairingTests.cs:272-284` | The acceptance criterion asks for integration tests for rate limits. Only `POST /pairing` (10/min) is tested. The poll limit (60/min) and the limit on `/session` token exchange (which shares the start policy and budget) have no tests. Concurrency has no tests either: two parallel polls get the token once, and two admins claiming the same code produce one screen. The code handles both with conditional updates. | Add tests for the poll and session limits, plus a parallel-poll test that asserts exactly one `paired` response and one `Set-Cookie`. |
| 5 | MINOR | `Pages/Admin/Screens/Detail.cshtml:63` | "Paired since" is shown in UTC, with a hard-coded `" UTC"` outside the localizer. The slide list uses the system time zone from Settings (`Slides/Index.cshtml.cs:46`, `ScheduleClock`). | Format with `ScheduleClock.ToWallClock` in the configured zone, as the slide pages do. Keep any suffix inside the resource string. |
| 6 | MINOR | `Pages/Admin/Screens/Detail.cshtml:80-84` | "Revoke" and "Create screen address" disconnect a live screen in one click, with no confirmation. The other destructive admin actions (delete) use a confirm step. | Add a confirmation step for both, or move them behind a small confirm page. |
| 7 | MINOR | `Features/Pairing/PairingService.cs:83,88` | If the claimed screen is deleted before delivery, the FK sets `ClaimedScreenId` to null while `ClaimedAt` stays set. The device keeps polling `pending` with a code that can no longer be claimed (`FindClaimableAsync` requires `ClaimedAt == null`) until it expires, which can take up to 15 min. | Treat "claimed but no screen" as `Gone` so the device gets a new code right away. |
| 8 | NIT | `tests/Signage.IntegrationTests/Infrastructure/PlayerClient.cs:13` | The `remoteIp` constructor parameter is never used (dead code). | Remove it, or use it for a per-client-IP rate-limit test. |
| 9 | NIT | `Features/Pairing/PairingService.cs:43-50` | After 20 collisions the code is used anyway, without a note. This is harmless at 25^6, but the docs say uniqueness is "enforced". | Throw or log after the loop, or reword ARCHITECTURE §2 to say "best effort". |

## Requirement check
| R-ID / criterion | Met? | Evidence (test name or file:line) |
|---|---|---|
| R-40 `/screen` on an unpaired device shows a large 6-character code from an unambiguous alphabet | Yes | `PairingCode.cs` alphabet; `PairingCodeTests.*`; `pairing.js` render; E2E `A_device_shows_a_code_the_admin_claims_it_…` |
| R-40 code valid 15 min, refreshed before expiry | Partly | `A_device_gets_a_formatted_code_valid_for_15_minutes…`, `Codes_expire_after_15_minutes…`. The refresh depends on the device clock (#3). |
| R-40 admin enters the code and picks a new or existing unpaired screen | Yes | `Pair.cshtml(.cs)`; `A_code_can_be_claimed_for_an_existing_unpaired_screen…`; `A_code_can_be_claimed_only_once` |
| R-40 device keeps a token (localStorage) | Yes, via an accepted deviation | ADR-0007 uses an HttpOnly cookie instead of localStorage. I agree: it's stronger, and the user-visible behaviour is the same. |
| One-time delivery, HttpOnly cookie | Yes | `After_an_admin_claims_the_code_the_device_receives_the_token_once_as_an_http_only_cookie` |
| R-41 pre-configured URL, saved and removed from the address bar | Partly | `A_screen_address_is_shown_once_exchanged_…`, E2E `A_pre_configured_screen_address_pairs_the_device…`. A transient failure at boot is fatal (#2). |
| R-42 re-pair | Yes | `Re_pairing_replaces_the_old_device` |
| R-42 revoke invalidates the token at once; a revoked device returns to pairing | Partly | `Revoking_ends_access_at_once_and_the_401_removes_the_cookie`, E2E revoke step. A pending claim bypasses it (#1). |
| R-53 256-bit tokens stored hashed; device secret hashed | Yes | `ScreenTokens.cs`; `ScreenTokensTests`; `…only_hashes_are_stored` |
| Rate limiting on pairing endpoints | Implemented, partly tested | `PlayerApi.cs:83-88,101,119,132`; only the start limit has a test (#4) |
| Authz: pairing, revoke and address are admin only | Yes | `AdminUi.cs` convention, `RequireAdmin()` in `Detail.cshtml.cs`; `Editors_cannot_pair_revoke_or_create_screen_addresses`; `RoleMatrixTests` |
| Antiforgery on admin posts | Yes | Tag-helper forms. The player API opts out on purpose, takes JSON or nothing, and has no ambient admin authority. |
| WP-05 NIT: nullable `Name` inputs | Yes | `GroupInput.cs`, `PlaylistDetailsInput.cs`, `Create.cshtml.cs`, `Detail.cshtml.cs` |
| Docs truthful / ADR | Yes | ARCHITECTURE §4, PLAYER_SPEC §8, ADR-0007 |
| Multi-replica | Yes | The only in-memory state is the per-replica rate limiter, which ADR-0007 documents. |
| Glossary | Yes | No "monitor", "card" or "feed" in the diff. |

## Notes for the product owner
- ADR-0007 keeps the screen token in an HttpOnly cookie, not in localStorage as R-40 says. I think that's the right call. Pairing still survives reboots, and scripts can't read the token.
- The rate limits count per replica and per client IP. Until WP-10 configures forwarded headers, every device behind a reverse proxy shares one budget. That budget includes the 60/min for polling, which is about three devices pairing at once.
