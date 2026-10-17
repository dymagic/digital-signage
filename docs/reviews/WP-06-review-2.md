# Review WP-06 — round 2
- Branch: wp-06-pairing @ 28b28ca
- Verdict: APPROVE
- Verified:
  - `dotnet build`: Build succeeded, 0 warnings, 0 errors.
  - `dotnet test`: Passed, total 383, failed 0 (372 in round 1, so 11 new tests). E2E tests are skipped outside Docker, as designed.
  - `scripts/test-e2e.sh`: exit 0, `Signage.E2E Total: 13, Errors: 0, Failed: 0, Skipped: 0`. This includes the new
    `A_kiosk_that_boots_before_the_server_answers_keeps_its_token_and_pairs_later`.
  - Read the full fix commit `a961c11` (source, tests, docs) and re-read `PairingService.cs`, `ScreenAccessService.cs`,
    `PlayerApi.cs`, `player.js`, `pairing.js`, `api.js`, `config.js` and PLAYER_SPEC §8 as they are now.

## Findings
| # | Severity | Location | Finding | Expected |
|---|---|---|---|---|
| 1 | MINOR | `Features/Pairing/PairingService.cs:190-203`, `wwwroot/screen/pairing.js:110-111`, `Pages/Admin/Screens/Pair.cshtml.cs:60` | **The claim window is longer than the time the device listens.** The device stops polling a code `pairingRefreshBeforeExpiryMs` (60 s) before it expires and shows a new one. `FindClaimableAsync` still accepts the old code until `ExpiresAt`. An admin who reads the code and types it during that last minute sees "Code accepted. The screen connects within a few seconds.", but the device never collects the token. The new expiry check in `PollAsync:88` has the same effect for a claim made in the last poll interval. Nothing is insecure (the claim simply expires), but the success message is false and the admin has to start again. This was already true in round 1; I missed it then. | Accept claims only while the device is still listening. For example, `FindClaimableAsync` could require `ExpiresAt > now + refresh margin (+ one poll interval)`, or the device could keep polling the old request until it expires. Add an integration test. This can be deferred to BACKLOG as a follow-up. |
| 2 | NIT | `Features/Pairing/ScreenAccessService.cs:279-286,295-303` | `RevokeAsync` and `CreateAddressTokenAsync` write `screens.token_hash` first and cancel pending claims afterwards, in separate statements outside a transaction. A `DeliverAsync` that runs between the two still marks its claim and overwrites the hash. The window is milliseconds and needs an admin click at the same moment as a device poll. | Cancel pending claims first, or run both statements in one transaction. A concurrent delivery is then either cancelled or overwritten by the revoke. |
| 3 | NIT | `Features/Pairing/PairingService.cs:180-187` | `ClaimAsync` cancels earlier pending claims for the screen *before* the conditional claim. If the claim then loses a race (`claimed == 0`), the earlier pending claim is gone too. This only matters for the existing-screen path, because the new-screen path rolls back. | Cancel only after `claimed == 1`, or wrap both in a transaction. |

## Requirement check
| R-ID / criterion | Met? | Evidence (test name or file:line) |
|---|---|---|
| R-40 large 6-character code from an unambiguous alphabet | Yes | `PairingCode.cs`; `PairingCodeTests.*`; E2E `A_device_shows_a_code_the_admin_claims_it_…` |
| R-40 code valid 15 min, refreshed before expiry | Yes | `The_device_is_told_how_long_its_code_lives_…` (900 s); `pairing.js:109-111` uses `performance.now()`. The claim/listen window mismatch is finding #1. |
| R-40 admin enters the code and picks a new or existing unpaired screen | Yes | `A_code_can_be_claimed_for_an_existing_unpaired_screen…`, `Parallel_claims_of_one_code_succeed_exactly_once` |
| R-40 device keeps a token | Yes, via ADR-0007 | HttpOnly cookie instead of localStorage. I accepted this in round 1. |
| One-time delivery, HttpOnly cookie | Yes | `After_an_admin_claims_the_code_the_device_receives_the_token_once_…`, `Parallel_polls_deliver_the_token_exactly_once` |
| R-41 pre-configured address, saved and removed from the address bar | Yes | `player.js:15-31` retries until the server gives a definite answer and only strips the token after that. E2E `A_kiosk_that_boots_before_the_server_answers_…`, `A_pre_configured_screen_address_pairs_the_device…` |
| R-42 re-pair | Yes | `Re_pairing_replaces_the_old_device`, `Only_the_newest_claim_for_a_screen_delivers_a_token` |
| R-42 revoke invalidates at once; the device returns to pairing | Yes | `Revoking_ends_access_at_once_…`, `A_revoke_before_the_device_collects_its_token_cancels_the_pairing`, `A_claimed_code_must_be_collected_before_it_expires`, E2E revoke step |
| R-53 256-bit tokens, stored hashed | Yes | `ScreenTokens.cs`, `ScreenTokensTests`, `…only_hashes_are_stored` |
| Rate limiting on pairing endpoints (tested) | Yes | `Polling_and_the_token_exchange_are_rate_limited_too`, plus the start-limit test from round 1 |
| Authz admin-only for pair, revoke and address | Yes | `Editors_cannot_pair_revoke_or_create_screen_addresses`, `RoleMatrixTests` |
| Antiforgery | Yes | Tag-helper forms. The player API opts out on purpose, as reviewed in round 1. |
| Docs truthful / ADR | Yes | ARCHITECTURE §2 now says "best effort: 20 tries", ADR-0007, PLAYER_SPEC §8 |
| Multi-replica | Yes | All pairing state is in Postgres. The rate limiter is per replica, as ADR-0007 documents. |
| Glossary | Yes | No "monitor", "card" or "feed" in the added lines. |

## Previous round follow-up
| # | Round 1 finding | Status |
|---|---|---|
| 1 | MAJOR: a pending claim can bypass revoke | **Fixed.** `CancelPendingClaimsAsync` runs from revoke, from address creation and on a newer claim. Delivery also requires `ClaimedScreenId == screenId && ExpiresAt > now` (`PairingService.cs:214`), and polling refuses expired claims. Tests cover revoke, address, expiry and newest-claim. A millisecond race remains (#2, NIT). |
| 2 | MAJOR: a kiosk booting before the server loses its token | **Fixed.** `exchangeToken` throws on network errors and non-401 failures, and `takeTokenFromUrl` retries every `retryAfterErrorMs`. The token leaves the URL only after a definite answer. The E2E test aborts twice and then succeeds. |
| 3 | MINOR: refresh depends on the device clock | **Fixed.** Uses `expiresInSeconds` and `performance.now()`, waits at least `lifetime/2`, and sleeps a poll interval before each poll. |
| 4 | MINOR: missing rate-limit and concurrency tests | **Fixed.** Poll and session limits, parallel polls (exactly one 200 and one Set-Cookie) and parallel claims (exactly one screen) are now tested. |
| 5 | MINOR: "Paired since" shown in UTC | **Fixed.** Uses `ScheduleClock` and the system zone, with the zone name inside the resource string. Tested. |
| 6 | MINOR: revoke/address with no confirmation | **Fixed.** `data-confirm` and `admin/admin.js`, with the messages in resx. The test checks the attribute, and the E2E tests accept the dialog. |
| 7 | MINOR: claimed screen deleted, device stuck | **Fixed.** `PollAsync:101`, `A_device_whose_claimed_screen_was_deleted_starts_over_with_a_new_code`. |
| 8 | NIT: unused `remoteIp` | **Fixed.** |
| 9 | NIT: silent collision fallback | **Fixed.** Logged warning; ARCHITECTURE says "best effort". |

## Notes for the product owner
- A kiosk whose server isn't up yet now waits on a blank screen and retries every 5 s until the server answers. A visible "connecting" state belongs to the connectivity work (PLAYER_SPEC §9), not to this package.
- Finding #1 can confuse whoever pairs a screen during the last minute of a code. I suggest a BACKLOG follow-up line.
