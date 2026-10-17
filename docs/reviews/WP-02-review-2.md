# Review WP-02 — round 2
- Branch: wp-02-auth @ 3e386c1
- Verdict: CHANGES_REQUIRED
- Verified:
  - `dotnet build`: Build succeeded, 0 warnings, 0 errors.
  - `dotnet test`: 159 total, 155 passed, 0 failed, 4 skipped (the 4 E2E tests, which only run inside Docker).
  - `scripts/test-e2e.sh`: exit 0, Signage.E2E total 4, failed 0.
  - Glossary scan (`grep -rniwE "card|cards|monitor|monitors|feed|feeds" src tests`): no hits.
  - Manual concurrency checks against `SIGNAGE_DB_PORT=55498 docker compose -p signage-review2 up -d --build`, driven with curl (real antiforgery tokens and cookies). I created an admin via `/admin/setup`, created an Editor "victim" via `/admin/users/create`, signed in with the temporary password and changed it to an "old" password. Then I ran 3 (and 16) parallel loops that sign in as the victim with the old password, which simulates an attacker who holds a leaked password:
    - **Admin "Reset password" while the loops run: 8 of 8 attempts returned HTTP 500.** The log shows `DbUpdateConcurrencyException … expected to affect 1 row(s), but actually affected 0` from `ResetPasswordAsync`. Afterwards the old password still signed in (302).
    - **Admin deactivation (edit form, freshly loaded `Input.Version` each time) while the loops run: 5 of 5 attempts were refused** with "Someone else changed this in the meantime". Afterwards the old password still signed in.
    - **The victim changes their own password on `/admin/account/password` while the loops run:** the page reported success (302). Afterwards the **old password signed in (302) and the new password was refused (200)**. The change was silently reverted.
    - During the same runs, the log shows 34 sign-ins **with the correct password** that were refused as "locked out" (out of 920 successes).
    - Then `docker compose -p signage-review2 down -v`.

## Findings
| # | Severity | Location | Finding | Expected |
|---|---|---|---|---|
| 1 | BLOCKER | `src/Signage.Web/Features/Users/UserAccounts.cs:203-207` | **A successful sign-in writes a stale password hash back (lost update).** `CheckPasswordWithLockoutAsync` reads the user snapshot (l.42 / l.127), spends a full PBKDF2 verification, then always runs `SET password_hash = hash`, where `hash` is the snapshot's hash unless a rehash was needed. If a password change or an admin reset commits inside that window, the sign-in overwrites it with the old hash. I reproduced it: with 3 parallel sign-in loops using the old password, a user's own password change reported success, yet afterwards the **old password worked and the new one didn't**. The same overwrite applies to an admin reset once it commits (see #2). The security stamp *does* rotate, so existing cookies die, but the attacker just signs in again with the old password. This defeats R-52 "reset passwords" and the user's own change as remediation steps after a leak. This was already in round 1's success path; I missed it then. | Never write `password_hash` on success unless a rehash is needed. When you do rehash, make it conditional on the hash you verified against (`… WHERE id = @id AND password_hash = @verifiedHash`). Add an integration test that interleaves a sign-in with a password change or reset deterministically, e.g. through a test hasher or hook that blocks inside verification. Assert that the new password works and the old one doesn't. |
| 2 | BLOCKER | `UserAccounts.cs:174-185, 204-207` together with `Data/Configurations/SystemConfigurations.cs:18` (`Version` is `xmin`), `UserAccounts.cs:222-239` and `:246-294` | **Anyone holding a user's password can stop an admin from resetting or deactivating that account.** Every password check writes the `users` row: the claim UPDATE, plus the reset UPDATE on success. That bumps `xmin`, which is the optimistic-concurrency token for human edits. As a result: (a) `ResetPasswordAsync` loads a tracked entity, hashes (slowly), saves, and throws an unhandled `DbUpdateConcurrencyException`, so the admin gets **HTTP 500** (8/8 in my run). (b) `UpdateAsync` (deactivate or role change) returns the conflict message every time (5/5). With just 3 parallel sign-in loops, the admin had no way through the UI to lock out an attacker who holds a leaked password, which is exactly when R-52 matters. Even without an attacker, an admin's reset can hit a 500 when the user happens to sign in at the same moment, and an edit form goes stale whenever the user signs in. | Keep the sign-in bookkeeping (`access_failed_count`, `lockout_end`, rehash) from bumping the concurrency token that human edits use. Options: move the lockout counters to their own table (e.g. `user_sign_in_state`), or replace `xmin` on `users` with an app-managed version column that only human edits increment. Make the reset unconditional on the version, or handle the concurrency exception: an admin reset must not 500. Add integration tests: a reset and a deactivation succeed while sign-ins for that user run in parallel, and afterwards the old password no longer signs in. |
| 3 | MINOR | `UserAccounts.cs:176-185`, `docs/adr/0004-admin-authentication.md:30-34` | The counter counts attempts *in flight*, not failures. When ≥5 sign-ins for one account overlap, even correct passwords are refused as "locked out" until one of them resets the counter (34 of 954 correct attempts in my run). Real admins rarely do this, so the impact is small. But the ADR says "a correct password gives its try back", while the code actually resets the counter to 0 and can refuse correct passwords under overlap. | Either decrement on success (`access_failed_count = GREATEST(access_failed_count - 1, 0)`, and clear `lockout_end` only if it was set by this claim) or keep the behaviour and make the ADR describe it accurately. |
| 4 | NIT | `tests/Signage.IntegrationTests/Admin/RoleMatrixTests.cs:1-3` | `using Microsoft.EntityFrameworkCore;` is placed between the `Signage.*` usings, so the order isn't consistent. | Sort the usings. |

## Requirement check
| R-ID / criterion | Met? | Evidence (test name or file:line) |
|---|---|---|
| R-50 Accounts, Identity hasher, lockout | Partly | The lockout is now bounded under concurrency: `SignInTests.A_burst_of_parallel_guesses_gets_at_most_the_allowed_number_of_password_checks` (asserts the counter is exactly 5 and the lockout is set, so at most 5 verifications ran), `After_a_lockout_expires_the_count_starts_again`, and `Repeated_failures_lock_the_account_until_the_lockout_expires`. **But a successful sign-in can revert a password change (#1).** |
| R-51 First run page / bootstrap env vars | Yes | `SetupTests.*`, `BootstrapTests.*` (unchanged since round 1). |
| R-52 Admins create/role/deactivate/reset; forced change | Partly | `UserAdminTests.*` pass sequentially. **But reset and deactivate can be blocked by concurrent sign-ins, and a reset can 500 (#2) or be reverted (#1).** |
| Deactivated user can't log in; cookie invalidated (security stamp) | Yes | `SessionInvalidationTests.*`. In the concurrent case, deactivation can be blocked (#2). |
| §3 matrix: every role/endpoint permission tested | Yes | `RoleMatrixTests.Editors_cannot_create_users`, `Editors_cannot_change_roles_or_deactivate_users_including_themselves`, `Editors_cannot_reset_anyones_password`, `Editors_cannot_change_settings`. Each asserts the forbidden redirect and unchanged rows. |
| R-53 Antiforgery, cookies, encoding, no secrets | Yes | Unchanged since round 1. Tests still pass. |
| R-03 time zone usable by the player | Yes | `SettingsService.cs:33` validates against the offered IANA list. `SettingsPageTests.Invalid_values_are_rejected("10", "Romance Standard Time" / "EST5EDT")`. |
| R-64 i18n | Yes | `ResourceCoverageTests`. No new UI strings without resources. |
| Glossary | Yes | `auth-card` renamed to `auth-panel`. The scan is clean. |
| Multi-replica readiness | Yes | The lockout claim is a single atomic UPDATE serialised by the Postgres row lock. No in-memory state. |
| Docs truthful | Mostly | The ADR-0004 wording on "gives its try back" doesn't match the code (#3). The ADR doesn't mention that sign-ins write the row that human edits version (#2). |

## Previous round follow-up
| Round-1 # | Status | Evidence |
|---|---|---|
| 1 MAJOR lockout bypass under concurrency | Fixed | `UserAccounts.cs:170-209`: the attempt is claimed atomically before verification, under `WHERE lockout_end IS NULL OR lockout_end <= now`. Postgres re-evaluates the WHERE clause after a concurrent update, so the 6th+ claims return no row. The burst test proves ≤5 verifications. (The fix introduced a new write on every sign-in, which surfaced #1 and #2 above.) |
| 2 MAJOR role-matrix POSTs | Fixed | The four new `RoleMatrixTests` facts assert both the redirect and unchanged state (role, active flag, hash, stamp, settings). |
| 3 MINOR timing oracle on locked accounts | Fixed | `UserAccounts.cs:187-191` runs a dummy verification. |
| 4 MINOR change-password brute force | Fixed | `UserAccounts.cs:143-149`, `SessionInvalidationTests.Wrong_current_passwords_on_the_change_page_count_towards_lockout`. |
| 5 MINOR Windows/alias time zones | Fixed | `SettingsService.cs:32-36` plus two new InlineData cases. |
| 6 MINOR `auth-card` | Fixed | Renamed to `auth-panel`. |
| 7 NIT redundant using | Fixed | Removed. |
| 8 NIT page-filter scope | Deferred, acceptable | A follow-up line was added to `docs/BACKLOG.md` under WP-04. |

## Notes for the product owner
- #1 and #2 together matter mainly in one situation: a password has leaked and an admin is trying to shut the account out. Today, an attacker who keeps signing in can make the admin's reset fail with an error page, block deactivation, and silently undo a password change. Both fixes are small and local to `UserAccounts` and the `users` mapping. I expect round 3 to be quick.
- The round-1 notes still apply: `Secure` cookie behind a TLS proxy depends on the forwarded-headers package, and sign-out doesn't revoke a cookie on the server.
